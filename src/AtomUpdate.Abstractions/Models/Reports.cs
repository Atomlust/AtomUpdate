using AtomUpdate.Core.Enums;

namespace AtomUpdate.Abstractions.Models;

/// <summary>安装上报（/api/report/install，内容对齐 04 §5.3；更新器失败落盘的状态文件也按此补报）。</summary>
public sealed class InstallReport
{
    public required string ProjectKey { get; init; }

    public required string AppId { get; init; }

    public required string InstallationId { get; init; }

    public string? MachineFingerprint { get; init; }

    public string? FromVersion { get; init; }

    public required string ToVersion { get; init; }

    public UpdateStage Stage { get; init; }

    public InstallMode InstallMode { get; init; }

    public bool Elevated { get; init; }

    public bool ElevationRejected { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? LogPath { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>检查上报（/api/stats/batch 事件之一）。</summary>
public sealed class CheckReport
{
    public required string ProjectKey { get; init; }

    public required string AppId { get; init; }

    public required string InstallationId { get; init; }

    public required string CurrentVersion { get; init; }

    public string? LatestVersion { get; init; }

    public bool HasUpdate { get; init; }

    public UpdateTrigger Trigger { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>下载上报（/api/stats/batch 事件之一）。</summary>
public sealed class DownloadReport
{
    public required string ProjectKey { get; init; }

    public required string AppId { get; init; }

    public required string InstallationId { get; init; }

    public required string FromVersion { get; init; }

    public required string ToVersion { get; init; }

    /// <summary>包类型（full/delta；供下载统计区分增量/全量；旧客户端缺省为空）。</summary>
    public AssetKind? Kind { get; init; }

    public bool Success { get; init; }

    public long Bytes { get; init; }

    public long DurationMs { get; init; }

    public string? ErrorCode { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>启动上报（安装/升级成功后的活跃回执）。</summary>
public sealed class StartupReport
{
    public required string ProjectKey { get; init; }

    public required string AppId { get; init; }

    public required string InstallationId { get; init; }

    public string? MachineFingerprint { get; init; }

    public required string Version { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
