namespace AtomUpdate.Core.Constants;

/// <summary>管理后台时间显示约定：默认东八区，时区以 IANA 标识存储（Windows 由 .NET 自动映射）。</summary>
public static class DisplayTimeZone
{
    /// <summary>默认时区（东八区）。未配置或配置非法时一律回落到它。</summary>
    public const string DefaultTimeZoneId = "Asia/Shanghai";

    /// <summary>设置项键（存 Settings KV 表，无表结构变更）。</summary>
    public const string SettingKey = "display.time_zone";

    /// <summary>设置页下拉选项：IANA 标识 + 中文标签，覆盖常见部署地。</summary>
    public static readonly IReadOnlyList<(string Id, string Label)> Common = new (string, string)[]
    {
        ("Asia/Shanghai", "中国标准时间（东八区）"),
        ("Asia/Hong_Kong", "香港时间（东八区）"),
        ("Asia/Taipei", "台北时间（东八区）"),
        ("Asia/Tokyo", "东京时间（东九区）"),
        ("Asia/Singapore", "新加坡时间（东八区）"),
        ("Asia/Seoul", "首尔时间（东九区）"),
        ("Asia/Dubai", "迪拜时间（东四区）"),
        ("Europe/Moscow", "莫斯科时间（东三区）"),
        ("Europe/Berlin", "柏林时间（中欧）"),
        ("Europe/London", "伦敦时间（GMT/夏令时）"),
        ("America/New_York", "纽约时间（美东）"),
        ("America/Chicago", "芝加哥时间（美中）"),
        ("America/Los_Angeles", "洛杉矶时间（美西）"),
        ("Australia/Sydney", "悉尼时间（东十区）"),
        ("UTC", "协调世界时（UTC+0）")
    };

    /// <summary>解析为 TimeZoneInfo；空值/未知标识回落东八区（平台运行在容器 UTC 时区，
    /// 直接用 ToLocalTime 会把用户看到的时间整体偏移，故显示层一律经此解析）。</summary>
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
    }

    /// <summary>校验是否为平台可识别的时区标识（供设置写入口使用）。</summary>
    public static bool IsValid(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
