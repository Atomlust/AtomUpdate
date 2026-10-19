using System.Globalization;
using System.Text.Json;
using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using FluentValidation;

namespace AtomUpdate.Api.Endpoints.Manage;

internal static class ManageHelpers
{
    internal static async Task<IResult?> ValidateAsync<T>(T request, IValidator<T> validator, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (result.IsValid)
        {
            return null;
        }

        return Results.BadRequest(new ErrorResponse
        {
            Code = ErrorCodes.InvalidRequest,
            Message = string.Join("; ", result.Errors.Select(e => e.ErrorMessage))
        });
    }

    internal static (int Page, int Size) Pager(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 20, 1, 200));

    internal static IResult NotFound(string code, string message) =>
        Results.NotFound(new ErrorResponse { Code = code, Message = message });

    internal static IResult Conflict(string message) =>
        Results.Conflict(new ErrorResponse { Code = ErrorCodes.Conflict, Message = message });

    internal static IResult Forbidden(string message) =>
        Results.Json(new ErrorResponse { Code = ErrorCodes.Forbidden, Message = message },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>解析存储根目录：相对路径（如 "uploads"）基于 ContentRoot 合成绝对路径，绝对路径原样返回。</summary>
    internal static string ResolveStorageRoot(IConfiguration config, IWebHostEnvironment env)
    {
        var root = config.GetValue<string>("Storage:Root");
        return root is null
            ? Path.Combine(env.ContentRootPath, "uploads")
            : Path.IsPathRooted(root) ? root : Path.Combine(env.ContentRootPath, root);
    }

    internal static IResult TenantRequired() =>
        Forbidden("访问令牌缺少 tenant_id，无法确定租户上下文");

    internal static string ActorOf(HttpContext context) =>
        context.User.FindFirst(AuthClaims.Subject)?.Value ?? "unknown";

    // Details 列是 jsonb：传入对象序列化为 JSON，务必不要传裸字符串
    internal static void Audit(AtomUpdateDbContext db, long tenantId, string actor, string action,
        string entityType, long entityId, object? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(CultureInfo.InvariantCulture),
            Details = details is null ? null : JsonSerializer.Serialize(details),
            CreatedAt = DateTimeOffset.UtcNow
        });
}
