using AtomUpdate.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AtomUpdate.Api.Settings;

/// <summary>系统设置读取/写入（scoped，随请求使用租户无关的全局表）。
/// 语义：DB 有值优先，无值回落代码默认值；无环境变量兜底层。</summary>
public sealed class SettingsService(AtomUpdateDbContext db)
{
    /// <summary>设置键与代码默认值（新增运营参数：加键值对 + SettingsDto/UpdateRequest 字段 + UI 项）。</summary>
    public const string KeyRetentionDays = "logging.retention_days";
    public const string KeyCheckLogPerAppLimit = "logging.check_log_per_app_limit";

    public const int DefaultRetentionDays = 7;
    public const int DefaultCheckLogPerAppLimit = 500;

    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<int> GetIntAsync(string key, int defaultValue, CancellationToken ct = default)
    {
        var raw = await db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return raw is null ? defaultValue : JsonSerializer.Deserialize<int>(raw, JsonOptions);
    }

    /// <summary>读取两项日志运营参数（清理服务每轮调用）。</summary>
    public async Task<(int RetentionDays, int CheckLogPerAppLimit)> GetLoggingAsync(CancellationToken ct = default) =>
        (await GetIntAsync(KeyRetentionDays, DefaultRetentionDays, ct),
         await GetIntAsync(KeyCheckLogPerAppLimit, DefaultCheckLogPerAppLimit, ct));

    public async Task SetIntAsync(string key, int value, string? updatedBy, CancellationToken ct = default)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (setting is null)
        {
            db.Settings.Add(new Data.Entities.Setting { Key = key, Value = json, UpdatedBy = updatedBy, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
