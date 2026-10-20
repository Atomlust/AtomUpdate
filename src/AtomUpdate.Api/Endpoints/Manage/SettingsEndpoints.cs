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

        // 时区是纯展示配置，租户管理员/普通成员也要按它显示时间，故只要求登录（group 已带 Manage 要求）
        group.MapGet("/display-timezone", GetTimeZoneAsync).Produces<DisplayTimeZoneDto>();
    }

    private static async Task<IResult> GetTimeZoneAsync(SettingsService settings, CancellationToken ct) =>
        Results.Ok(new DisplayTimeZoneDto { TimeZoneId = await settings.GetTimeZoneAsync(ct) });

    private static async Task<IResult> GetAsync(SettingsService settings, CancellationToken ct)
    {
        var (retentionDays, limit) = await settings.GetLoggingAsync(ct);
        return Results.Ok(new SettingsDto
        {
            RetentionDays = retentionDays,
            CheckLogPerAppLimit = limit,
            TimeZoneId = await settings.GetTimeZoneAsync(ct)
        });
    }

    private static async Task<IResult> UpdateAsync(AtomUpdateDbContext db, SettingsService settings,
        SettingsUpdateRequest request, HttpContext http, CancellationToken ct)
    {
        var (currentRetention, currentLimit) = await settings.GetLoggingAsync(ct);
        var actor = ManageHelpers.ActorOf(http);
        var changes = new Dictionary<string, object>();

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

        if (request.TimeZoneId is { } timeZoneId)
        {
            var normalized = timeZoneId.Trim();
            // 空串 = 恢复默认东八区（落库为空值，读取侧回落默认值）
            if (normalized.Length > 0 && !DisplayTimeZone.IsValid(normalized))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(SettingsUpdateRequest.TimeZoneId)] =
                    [$"无法识别的时区标识：{normalized}。请使用 IANA 标识（如 Asia/Shanghai、UTC）。"]
                });
            }

            var current = await settings.GetTimeZoneAsync(ct);
            var target = normalized.Length == 0 ? DisplayTimeZone.DefaultTimeZoneId : normalized;
            if (!string.Equals(current, target, StringComparison.Ordinal))
            {
                await settings.SetStringAsync(DisplayTimeZone.SettingKey, target, actor, ct);
                changes["timeZoneId"] = target;
            }
        }

        if (changes.Count > 0)
        {
            // 平台级操作：审计租户固定 0（与平台管理员会话 tenant_id 一致）；EntityId 无业务主键，置 0，键名入 details
            ManageHelpers.Audit(db, 0, actor, "settings.update", "Setting", 0, new { changes });
            await db.SaveChangesAsync(ct);
        }

        var (retentionDays, checkLogPerAppLimit) = await settings.GetLoggingAsync(ct);
        return Results.Ok(new SettingsDto
        {
            RetentionDays = retentionDays,
            CheckLogPerAppLimit = checkLogPerAppLimit,
            TimeZoneId = await settings.GetTimeZoneAsync(ct)
        });
    }
}
