using AtomUpdate.Core.Enums;

namespace AtomUpdate.Core.Models;

public sealed class UpdatePolicy
{
    public required int Version { get; init; }
    public bool PollingEnabled { get; init; }
    public int PollingIntervalMinutes { get; init; }
    public int PollingJitterMinutes { get; init; }
    public bool DownloadOnStartup { get; init; }
    public bool DownloadOnPolling { get; init; }
    public bool DownloadOnManualCheck { get; init; }
    public bool DownloadOnForceUpdate { get; init; }
    public bool DownloadOnNetworkRecovery { get; init; }
    public bool DownloadOnResume { get; init; }
    public InstallMode InstallMode { get; init; }
    public bool AllowForceUpdate { get; init; }
    public bool AllowDowngrade { get; init; }
    public bool AllowPostpone { get; init; }
    public int MaxPostponeCount { get; init; }
    public bool AllowMeteredNetwork { get; init; }
    public int MaxDownloadSpeedKBps { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
