using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Endpoints.Manage;

internal static class AuthEndpoints
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        // 登录为匿名端点（凭据本身即凭证），仍挂在 /api/manage 前缀下
        group.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .Produces<LoginResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);
        group.MapPost("/auth/change-password", ChangePasswordAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);
        // 首次部署设置向导（零配置部署，docs/09）：公开，但仅用户表为空时可用（表非空一律 409）
        group.MapGet("/auth/setup/status", SetupStatusAsync)
            .AllowAnonymous()
            .Produces<SetupStatusResponse>();
        group.MapPost("/auth/setup", SetupAsync)
            .AllowAnonymous()
            .Produces<LoginResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request,
        IValidator<LoginRequest> validator, AtomUpdateDbContext db, AuthTokenService tokens, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        // 登录标识：用户名或邮箱。Email/UserName 全局唯一（跨租户），登录先于租户上下文，忽略租户过滤器。
        // UserName 存储前已规范化为小写，此处输入同样归一小写后精确匹配；Email 保持原值精确匹配。
        var identifier = request.Email.Trim();
        var userNameKey = identifier.ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(
            u => (u.UserName != null && u.UserName == userNameKey) || u.Email == identifier, ct);
        if (user is null || user.Status != EntityStatus.Active ||
            !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            // 统一错误口径，不区分"账号不存在/密码错误/已停用"，避免账号枚举
            return Results.Json(new ErrorResponse
                {
                    Code = ErrorCodes.InvalidCredentials,
                    Message = "用户名或邮箱或密码错误，或账号已停用"
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var (token, expiresAt) = tokens.Issue(user);
        return Results.Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = ToDto(user)
        });
    }

    /// <summary>用户名存储规范化：Trim → 空则 null → 小写（与登录查询的归一约定一致）。</summary>
    internal static string? NormalizeUserName(string? raw) =>
        raw?.Trim() is { Length: > 0 } trimmed ? trimmed.ToLowerInvariant() : null;

    internal static UserDto ToDto(User user) => ToDto(user, bootstrapEmail: null);

    internal static UserDto ToDto(User user, string? bootstrapEmail) => new()
    {
        Id = user.Id,
        Email = user.Email,
        UserName = user.UserName,
        DisplayName = user.DisplayName,
        IsPlatformAdmin = user.IsPlatformAdmin,
        Status = user.Status,
        CreatedAt = user.CreatedAt,
        IsBootstrapAdmin = bootstrapEmail is { Length: > 0 }
            && string.Equals(user.Email, bootstrapEmail, StringComparison.OrdinalIgnoreCase)
    };

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request,
        AtomUpdateDbContext db, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return Results.BadRequest(new ErrorResponse { Code = ErrorCodes.InvalidRequest, Message = "新密码至少 8 位" });
        }

        if (!long.TryParse(http.User.FindFirst(AuthClaims.Subject)?.Value, out var userId))
        {
            return Results.Unauthorized();
        }

        // 当前用户查自己：忽略租户过滤器（密码修改不应受租户隔离影响）
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !PasswordHasher.Verify(request.OldPassword, user.PasswordHash))
        {
            return Results.Json(new ErrorResponse { Code = ErrorCodes.InvalidCredentials, Message = "原密码错误" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        ManageHelpers.Audit(db, user.TenantId, ManageHelpers.ActorOf(http), "user.change_password", "User", user.Id);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetupStatusAsync(AtomUpdateDbContext db, CancellationToken ct) =>
        Results.Ok(new SetupStatusResponse { Required = !await db.Users.IgnoreQueryFilters().AnyAsync(ct) });

    private static async Task<IResult> SetupAsync(SetupRequest request, IValidator<SetupRequest> validator,
        AtomUpdateDbContext db, AuthTokenService tokens, IConfiguration configuration, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        // 守卫：仅用户表为空可初始化（表非空一律 409，已初始化系统不可借向导再造账号）。
        // 事务内二次检查收窄并发窗口；即便极端并发下创建出两个管理员，仍属部署方完成首次初始化前的竞争，不破坏既有数据
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(ct))
        {
            return Results.Json(new ErrorResponse { Code = ErrorCodes.Conflict, Message = "系统已初始化，请直接登录" },
                statusCode: StatusCodes.Status409Conflict);
        }

        var tenantSlug = configuration.GetSection("Auth:Bootstrap")["TenantSlug"] is { Length: > 0 } slug
            ? slug : "default";
        var tenant = await AuthBootstrapSeeder.EnsureDefaultTenantAsync(db, tenantSlug);

        var email = request.Email.Trim();
        var user = new User
        {
            TenantId = tenant.Id,
            Email = email,
            UserName = NormalizeUserName(request.UserName) ?? "admin",
            PasswordHash = PasswordHasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "平台管理员" : request.DisplayName.Trim(),
            IsPlatformAdmin = true,
            Status = EntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        ManageHelpers.Audit(db, tenant.Id, "setup", "user.setup", "User", user.Id,
            new { user.Email, user.UserName });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // 直接签发会话：向导完成后无需再走登录页
        var (token, expiresAt) = tokens.Issue(user);
        return Results.Ok(new LoginResponse { Token = token, ExpiresAt = expiresAt, User = ToDto(user) });
    }
}
