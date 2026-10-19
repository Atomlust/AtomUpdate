namespace AtomUpdate.Api.Data.Entities;

/// <summary>系统设置（全局键值，平台级，不随租户过滤）：DB 有值优先于代码默认值；
/// 平台管理员经 /api/manage/settings 维护，运营参数（日志保留等）免重启即时生效。</summary>
public class Setting
{
    /// <summary>设置键（主键，如 logging.retention_days）。</summary>
    public required string Key { get; set; }

    /// <summary>JSON 序列化的值。</summary>
    public required string Value { get; set; }

    /// <summary>最近修改者（JWT subject）。</summary>
    public string? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
