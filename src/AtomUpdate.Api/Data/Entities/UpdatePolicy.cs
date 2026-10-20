using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>应用级更新策略（MVP 粒度 App 级；Channel 枚举字段供通道级覆盖二阶段启用）。</summary>
public sealed class UpdatePolicy : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long AppId { get; set; }

    /// <summary>通道级覆盖保留位（MVP 仅 App 级，null）。</summary>
    public UpdateChannel? Channel { get; set; }

    /// <summary>原 Channel 实体属性随通道枚举化并入策略域（默认防降级；强制更新默认门控关闭）。</summary>
    public bool AllowForceUpdate { get; set; }
    public bool AllowDowngrade { get; set; }

    /// <summary>修改时同一事务内自增，驱动客户端策略分发与 ETag。</summary>
    public int Version { get; set; } = 1;

    public bool PollingEnabled { get; set; } = true;
    public int PollingIntervalMinutes { get; set; } = 360;
    public int PollingJitterMinutes { get; set; } = 10;
    public bool DownloadOnStartup { get; set; } = true;
    public bool DownloadOnPolling { get; set; }
    public bool DownloadOnManualCheck { get; set; } = true;
    public bool DownloadOnForceUpdate { get; set; } = true;
    public bool DownloadOnNetworkRecovery { get; set; }
    public bool DownloadOnResume { get; set; }
    public InstallMode InstallMode { get; set; } = InstallMode.OnRestart;
    public bool AllowPostpone { get; set; } = true;
    public int MaxPostponeCount { get; set; } = 3;
    public bool AllowMeteredNetwork { get; set; }
    public int MaxDownloadSpeedKBps { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
