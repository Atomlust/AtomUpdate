namespace AtomUpdate.Updater.Report;

/// <summary>安装回传内容（04 §5.3）：POST /api/report/install。更新器不引用 Core，模型本地定义。</summary>
public sealed class InstallReport
{
    public string? ProjectKey { get; set; }

    public string? AppId { get; set; }

    public string? InstallationId { get; set; }

    public string? MachineFingerprint { get; set; }

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    public string? Stage { get; set; }

    public string? InstallMode { get; set; }

    public bool Elevated { get; set; }

    public bool ElevationRejected { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? LogPath { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
