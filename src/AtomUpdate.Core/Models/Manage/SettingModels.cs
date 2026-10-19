namespace AtomUpdate.Core.Models.Manage;

/// <summary>系统设置（平台级运营参数；DB 有值优先，未设置项返回代码默认值）。</summary>
public sealed class SettingsDto
{
    /// <summary>运行日志（检测/下载/安装上报）按时间保留天数；≤0 表示关闭时间清理。</summary>
    public required int RetentionDays { get; init; }

    /// <summary>检测记录每应用保留条数上限；≤0 表示关闭数量裁剪。</summary>
    public required int CheckLogPerAppLimit { get; init; }
}

/// <summary>系统设置更新（null = 不修改该项）。</summary>
public sealed class SettingsUpdateRequest
{
    public int? RetentionDays { get; init; }
    public int? CheckLogPerAppLimit { get; init; }
}
