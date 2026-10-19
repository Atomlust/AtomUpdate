namespace AtomUpdate.Updater.State;

/// <summary>更新器状态文件（04 §4.4，state/{appId}/updater-state.json）。SDK 启动时读取未回传状态补报（M4 集成）。</summary>
public sealed class UpdaterState
{
    public string? InstallationId { get; set; }

    public string? MachineFingerprint { get; set; }

    public string? ProjectKey { get; set; }

    public string? AppId { get; set; }

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    /// <summary>starting/verifying/installing/completed/failed/rolledBack。</summary>
    public string Stage { get; set; } = StateStages.Starting;

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

    /// <summary>由更新器版本串（自升级替换后可识别旧版本写入的状态）。</summary>
    public string? UpdaterVersion { get; set; }
}

public static class StateStages
{
    public const string Starting = "starting";
    public const string Verifying = "verifying";
    public const string Installing = "installing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string RolledBack = "rolledBack";
}

public static class StateResults
{
    public const string Success = "success";
    public const string Failed = "failed";
    public const string RolledBack = "rolledBack";
}
