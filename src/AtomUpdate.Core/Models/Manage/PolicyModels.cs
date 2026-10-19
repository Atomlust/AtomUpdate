using AtomUpdate.Core.Enums;

namespace AtomUpdate.Core.Models.Manage;

public sealed class PolicyDto
{
    public required int Version { get; init; }
    public required bool PollingEnabled { get; init; }
    public required int PollingIntervalMinutes { get; init; }
    public required int PollingJitterMinutes { get; init; }
    public required bool DownloadOnStartup { get; init; }
    public required bool DownloadOnPolling { get; init; }
    public required bool DownloadOnManualCheck { get; init; }
    public required bool DownloadOnForceUpdate { get; init; }
    public required bool DownloadOnNetworkRecovery { get; init; }
    public required bool DownloadOnResume { get; init; }
    public required InstallMode InstallMode { get; init; }
    public required bool AllowForceUpdate { get; init; }
    public required bool AllowDowngrade { get; init; }
    public required bool AllowPostpone { get; init; }
    public required int MaxPostponeCount { get; init; }
    public required bool AllowMeteredNetwork { get; init; }
    public required int MaxDownloadSpeedKBps { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

public sealed class PolicyUpdateRequest
{
    public bool? PollingEnabled { get; init; }
    public int? PollingIntervalMinutes { get; init; }
    public int? PollingJitterMinutes { get; init; }
    public bool? DownloadOnStartup { get; init; }
    public bool? DownloadOnPolling { get; init; }
    public bool? DownloadOnManualCheck { get; init; }
    public bool? DownloadOnForceUpdate { get; init; }
    public bool? DownloadOnNetworkRecovery { get; init; }
    public bool? DownloadOnResume { get; init; }
    public InstallMode? InstallMode { get; init; }
    public bool? AllowForceUpdate { get; init; }
    public bool? AllowDowngrade { get; init; }
    public bool? AllowPostpone { get; init; }
    public int? MaxPostponeCount { get; init; }
    public bool? AllowMeteredNetwork { get; init; }
    public int? MaxDownloadSpeedKBps { get; init; }
}
