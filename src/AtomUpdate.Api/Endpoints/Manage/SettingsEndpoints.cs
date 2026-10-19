using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Settings;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Api.Endpoints.Manage;

/// <summary>系统设置（docs/03）：平台级运营参数，DB 有值优先于代码默认值，修改免重启即时生效。</summary>
internal static class SettingsEndpoints
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        var platform = group.MapGroup("").RequireAuthorization(AuthPolicies.PlatformAdmin);

        platform.MapGet("/settings", GetAsync).Produces<SettingsDto>();
        platform.MapPatch("/settings", UpdateAsync).Produces<SettingsDto>();
    }

    private static async Task<IResult> GetAsync(SettingsService settings, CancellationToken ct)
    {
        var (retentionDays, limit) = await settings.GetLoggingAsync(ct);
        return Results.Ok(new SettingsDto { RetentionDays = retentionDays, CheckLogPerAppLimit = limit });
    }

    private static async Task<IResult> UpdateAsync(AtomUpdateDbContext db, SettingsService settings,
        SettingsUpdateRequest request, HttpContext http, CancellationToken ct)
    {
        var (currentRetention, currentLimit) = await settings.GetLoggingAsync(ct);
        var actor = ManageHelpers.ActorOf(http);
        var changes = new Dictionary<string, int>();

        if (request.RetentionDays is { } retention && retention != currentRetention)
        {
            await settings.SetIntAsync(SettingsService.KeyRetentionDays, retention, actor, ct);
            changes["retentionDays"] = retention;
        }

        if (request.CheckLogPerAppLimit is { } limit && limit != currentLimit)
        {
            await settings.SetIntAsync(SettingsService.KeyCheckLogPerAppLimit, limit, actor, ct);
            changes["checkLogPerAppLimit"] = limit;
        }

        if (changes.Count > 0)
        {
            // 平台级操作：审计租户固定 0（与平台管理员会话 tenant_id 一致）；EntityId 无业务主键，置 0，键名入 details
            ManageHelpers.Audit(db, 0, actor, "settings.update", "Setting", 0, new { changes });
            await db.SaveChangesAsync(ct);
        }

        var (retentionDays, checkLogPerAppLimit) = await settings.GetLoggingAsync(ct);
        return Results.Ok(new SettingsDto { RetentionDays = retentionDays, CheckLogPerAppLimit = checkLogPerAppLimit });
    }
}
