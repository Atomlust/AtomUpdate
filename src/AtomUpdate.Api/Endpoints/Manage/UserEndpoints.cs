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

internal static class UserEndpoints
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/users", ListAsync)
            .Produces<PagedResult<UserDto>>();
        group.MapPost("/users", CreateAsync)
            .Produces<UserDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
        group.MapPatch("/users/{id:long}", UpdateAsync)
            .Produces<UserDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapDelete("/users/{id:long}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(AtomUpdateDbContext db, IConfiguration configuration,
        int? page, int? pageSize, string? keyword, CancellationToken ct)
    {
        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.Users.AsNoTracking();
        if (keyword?.Trim() is { Length: > 0 } kw)
        {
            var pattern = $"%{kw}%";
            // npgsql→ILike；SQLite 无 ILike 翻译，退化 Like（其 LIKE 对 ASCII 默认不区分大小写，行为对齐）
            query = db.Database.IsSqlite()
                ? query.Where(u => EF.Functions.Like(u.Email, pattern)
                    || (u.UserName != null && EF.Functions.Like(u.UserName, pattern))
                    || (u.DisplayName != null && EF.Functions.Like(u.DisplayName, pattern)))
                : query.Where(u => EF.Functions.ILike(u.Email, pattern)
                    || (u.UserName != null && EF.Functions.ILike(u.UserName, pattern))
                    || (u.DisplayName != null && EF.Functions.ILike(u.DisplayName, pattern)));
        }

        var ordered = query.OrderBy(u => u.Id);
        var total = await ordered.CountAsync(ct);
        var items = await ordered.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var bootstrapEmail = configuration["Auth:Bootstrap:Email"];
        return Results.Ok(new PagedResult<UserDto> { Items = items.Select(u => AuthEndpoints.ToDto(u, bootstrapEmail)).ToList(), Total = total });
    }

    private static async Task<IResult> CreateAsync(UserCreateRequest request,
        IValidator<UserCreateRequest> validator, AtomUpdateDbContext db, HttpContext http, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        // Email 全局唯一（跨租户）
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == request.Email, ct))
        {
            return Results.Conflict(new ErrorResponse { Code = ErrorCodes.UserExists, Message = "邮箱已被使用" });
        }

        // UserName 可选登录名：规范化后全局唯一（部分唯一索引兜底，此处先行查重给出友好错误）
        var userName = AuthEndpoints.NormalizeUserName(request.UserName);
        if (userName is not null &&
            await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName, ct))
        {
            return Results.Conflict(new ErrorResponse { Code = ErrorCodes.UserExists, Message = "用户名已被使用" });
        }

        var tenantId = TenantIdOf(http);
        if (tenantId is null)
        {
            return ManageHelpers.TenantRequired();
        }

        var isPlatformAdmin = request.IsPlatformAdmin == true && http.User.HasClaim(AuthClaims.PlatformAdmin, "true");
        var user = new User
        {
            TenantId = tenantId.Value,
            Email = request.Email,
            UserName = userName,
            PasswordHash = PasswordHasher.Hash(request.Password),
            DisplayName = request.DisplayName,
            IsPlatformAdmin = isPlatformAdmin,
            Status = EntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        ManageHelpers.Audit(db, tenantId.Value, ManageHelpers.ActorOf(http), "user.create", "User", user.Id,
            new { email = user.Email, userName = user.UserName, isPlatformAdmin = user.IsPlatformAdmin });
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/manage/users/{user.Id}", AuthEndpoints.ToDto(user));
    }

    private static async Task<IResult> UpdateAsync(long id, UserUpdateRequest request,
        IValidator<UserUpdateRequest> validator, AtomUpdateDbContext db, HttpContext http, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        // User 是租户作用域实体：查询过滤器保证仅本租户可见/可改
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.UserNotFound, "用户不存在");
        }

        if (request.DisplayName is { } displayName)
        {
            user.DisplayName = displayName;
        }

        // 用户名修改：null=不改；空串=清除（仅邮箱登录）；非空=规范化后设置（查重排除自身）
        if (request.UserName is { } rawUserName)
        {
            var userName = AuthEndpoints.NormalizeUserName(rawUserName);
            if (userName is not null &&
                await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName && u.Id != id, ct))
            {
                return Results.Conflict(new ErrorResponse { Code = ErrorCodes.UserExists, Message = "用户名已被使用" });
            }

            user.UserName = userName;
        }

        if (request.Status is { } status)
        {
            user.Status = status;
        }

        if (request.Password is { } password)
        {
            user.PasswordHash = PasswordHasher.Hash(password);
        }

        if (request.IsPlatformAdmin is { } platformAdmin && http.User.HasClaim(AuthClaims.PlatformAdmin, "true"))
        {
            user.IsPlatformAdmin = platformAdmin;
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        ManageHelpers.Audit(db, user.TenantId, ManageHelpers.ActorOf(http), "user.update", "User", user.Id);
        await db.SaveChangesAsync(ct);

        return Results.Ok(AuthEndpoints.ToDto(user));
    }

    private static async Task<IResult> DeleteAsync(long id, AtomUpdateDbContext db, IConfiguration configuration,
        HttpContext http, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.UserNotFound, "用户不存在");
        }

        // 不允许自删
        if (long.TryParse(http.User.FindFirst(AuthClaims.Subject)?.Value, out var subject) && subject == id)
        {
            return Results.BadRequest(new ErrorResponse { Code = ErrorCodes.InvalidRequest, Message = "不能删除当前登录用户" });
        }

        // 引导创建的超级管理员不可删除（Email 不可变更，匹配配置邮箱即内置账号）
        var bootstrapEmail = configuration["Auth:Bootstrap:Email"];
        if (bootstrapEmail is { Length: > 0 } && string.Equals(user.Email, bootstrapEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = ErrorCodes.InvalidRequest,
                Message = "引导管理员账号不可删除"
            });
        }

        db.Users.Remove(user);
        ManageHelpers.Audit(db, user.TenantId, ManageHelpers.ActorOf(http), "user.delete", "User", user.Id,
            new { email = user.Email });
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static long? TenantIdOf(HttpContext http) =>
        long.TryParse(http.User.FindFirst(AuthClaims.TenantId)?.Value, out var tenantId) ? tenantId : null;
}
