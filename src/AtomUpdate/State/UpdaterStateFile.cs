namespace AtomUpdate.State;

/// <summary>更新器状态文件快照（04 §4.4 文档化格式，state/{appId}/updater-state.json）。
/// SDK 启动时读取未回传（reported=false）状态补报，回写 reported=true；字段与更新器 UpdaterState 一一对应。</summary>
public sealed class UpdaterStateFile
{
    public string? InstallationId { get; set; }

    public string? MachineFingerprint { get; set; }

    public string? ProjectKey { get; set; }

    public string? AppId { get; set; }

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    /// <summary>starting/verifying/installing/completed/failed/rolledBack。</summary>
    public string? Stage { get; set; }

    /// <summary>success/failed/rolledBack（终态前为 null）。</summary>
    public string? Result { get; set; }

    public string? Reason { get; set; }

    public string? InstallMode { get; set; }

    public bool Elevated { get; set; }

    public bool ElevationRejected { get; set; }

    /// <summary>十六进制错误码（如 "0x1004"）。</summary>
    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? LogPath { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public int RetryCount { get; set; }

    /// <summary>回传是否已送达；false 等 SDK 补报。</summary>
    public bool Reported { get; set; }

    public string? UpdaterVersion { get; set; }
}
