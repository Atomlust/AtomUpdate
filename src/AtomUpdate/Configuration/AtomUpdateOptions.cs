using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Core.Enums;
using Microsoft.Extensions.Logging;

namespace AtomUpdate.Configuration;

/// <summary>SDK 配置根（04 §3.7）。优先级：内置默认 → appsettings.json → ATOMUPDATE_ 环境变量 → 代码覆盖 → 服务端下发策略（仅策略类字段）。</summary>
public sealed class AtomUpdateOptions
{
    public const string SectionName = "AtomUpdate";

    /// <summary>更新服务器地址，如 https://update.example.com。</summary>
    public required string ServerUrl { get; set; }

    public required string ProjectKey { get; set; }

    public required string AppId { get; set; }

    /// <summary>主程序当前版本。</summary>
    public required string CurrentVersion { get; set; }

    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;

    public PollingOptions Polling { get; set; } = new();

    public DownloadOptions Download { get; set; } = new();

    public VerifyOptions Verify { get; set; } = new();

    public InstallOptions Install { get; set; } = new();

    public NotifyOptions Notify { get; set; } = new();

    public CacheOptions Cache { get; set; } = new();

    public UpdaterOptions Updater { get; set; } = new();

    public ReportOptions Report { get; set; } = new();

    public NetworkOptions Network { get; set; } = new();

    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>轮询配置。Backoff：失败退避 Initial ×2^n，上限 Max。</summary>
public sealed class PollingOptions
{
    public bool Enabled { get; set; } = true;

    public bool CheckOnStartup { get; set; } = true;

    /// <summary>轮询间隔（默认 6 小时）。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>抖动幅度 ± 分钟（默认 10）。</summary>
    public int JitterMinutes { get; set; } = 10;

    public bool CheckOnNetworkRecovery { get; set; } = true;

    public bool CheckOnResume { get; set; } = true;

    /// <summary>启动后首次检查延迟（默认 5 秒）。</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan BackoffInitial { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan BackoffMax { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>退避倍率（默认 2）。</summary>
    public double BackoffFactor { get; set; } = 2;
}

/// <summary>下载配置（04 §3.7 六场景模式 + 无进展超时）。</summary>
public sealed class DownloadOptions
{
    /// <summary>各触发场景的默认下载模式：Startup/ManualCheck/ForceUpdate=Foreground，Polling/NetworkRecovery/Resume=Silent。</summary>
    public Dictionary<UpdateTrigger, DownloadMode> ModeByTrigger { get; set; } = new()
    {
        [UpdateTrigger.Startup] = DownloadMode.Foreground,
        [UpdateTrigger.Manual] = DownloadMode.Foreground,
        [UpdateTrigger.Force] = DownloadMode.Foreground,
        [UpdateTrigger.Polling] = DownloadMode.Silent,
        [UpdateTrigger.NetworkRecovery] = DownloadMode.Silent,
        [UpdateTrigger.Resume] = DownloadMode.Silent
    };

    public DownloadMode GetMode(UpdateTrigger trigger) =>
        ModeByTrigger.GetValueOrDefault(trigger, DownloadMode.Silent);

    /// <summary>允许计量网络下载（默认禁止）。</summary>
    public bool AllowMeteredNetwork { get; set; }

    public int MaxConcurrentDownloads { get; set; } = 1;

    /// <summary>限速 KB/s；0 = 不限。</summary>
    public int MaxDownloadSpeedKBps { get; set; }

    /// <summary>低电量暂停（默认开启）。</summary>
    public bool PauseOnLowBattery { get; set; } = true;

    /// <summary>失败重试次数。</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>无进展超时秒数（60s 无字节进展才中断；0 = 不启用）。</summary>
    public int StallTimeoutSeconds { get; set; } = 60;

    /// <summary>下载临时目录（null = cache/{appId}/downloads/）。</summary>
    public string? TempDirectory { get; set; }
}

/// <summary>校验配置。开关仅本地配置，服务端策略不可覆盖（防降级攻击向量）。SHA256 始终强制。</summary>
public sealed class VerifyOptions
{
    /// <summary>默认不验签；置 true 启用签名验证（公钥内嵌分发，不从服务端获取）。</summary>
    public bool RequireSignature { get; set; }
}

/// <summary>安装与推迟配置。</summary>
public sealed class InstallOptions
{
    public InstallMode Mode { get; set; } = InstallMode.OnRestart;

    public bool AllowPostpone { get; set; } = true;

    public int MaxPostponeCount { get; set; } = 3;

    /// <summary>推迟截止时长（超期进入强制路径；null = 不限）。</summary>
    public TimeSpan? PostponeDeadline { get; set; }

    /// <summary>允许降级安装（默认禁止）。</summary>
    public bool AllowDowngrade { get; set; }

    public string RestartArgs { get; set; } = "--skip-update-check";
}

/// <summary>通知配置。</summary>
public sealed class NotifyOptions
{
    public NotifyMode Mode { get; set; } = NotifyMode.Auto;

    public bool ShowReleaseNotes { get; set; } = true;

    public bool ShowOnReady { get; set; } = true;

    public bool ShowOnDownloadFailed { get; set; }

    /// <summary>标题模板（{version} 占位）。</summary>
    public string TitleTemplate { get; set; } = "发现新版本 {version}";

    /// <summary>正文模板（{version} 占位）。</summary>
    public string? BodyTemplate { get; set; }
}

/// <summary>缓存配置（04 §3.8/§3.14）。</summary>
public sealed class CacheOptions
{
    public bool EnablePolicyCache { get; set; } = true;

    public bool EnableETag { get; set; } = true;

    /// <summary>上报失败落盘待补报。</summary>
    public bool EnableOfflineReport { get; set; } = true;

    /// <summary>缓存根目录（null = 平台默认 AtomUpdate 根目录）。</summary>
    public string? CacheDirectory { get; set; }

    /// <summary>策略缓存过期时长（null = 永不过期，仅被新下发覆盖）。</summary>
    public TimeSpan? CacheExpiration { get; set; }
}

/// <summary>更新器配置（04 §5.2）。</summary>
/// <summary>更新器配置（04 §5.2）。更新器自身升级不在 SDK 侧开关——由更新器配置的 `selfUpdate.*` 决定（04 §4.12/§4.13）。</summary>
public sealed class UpdaterOptions
{
    /// <summary>自定义更新器路径（null = 释放内嵌更新器到版本化目录）。</summary>
    public string? UpdaterPath { get; set; }

    public bool AutoExtractUpdater { get; set; } = true;

    /// <summary>更新器等待主程序退出的秒数。</summary>
    public int WaitForMainProcessExit { get; set; } = 5;
}

/// <summary>上报配置（04 §3.7 Report 节 + 离线兜底）。</summary>
public sealed class ReportOptions
{
    public bool Enabled { get; set; } = true;

    public bool ReportOnStartup { get; set; } = true;

    public bool ReportMachineFingerprint { get; set; } = true;

    public bool ReportInstallationId { get; set; } = true;

    public int RetryCount { get; set; } = 3;

    /// <summary>单次上报超时（默认 15 秒）。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>失败落盘待补报（默认开启）。</summary>
    public bool FallbackToLocal { get; set; } = true;
}

/// <summary>网络配置（04 §3.10）。</summary>
public sealed class NetworkOptions
{
    /// <summary>代理地址（null = 系统默认）。</summary>
    public string? Proxy { get; set; }

    /// <summary>检查/上报 HTTP 超时（下载使用无进展超时）。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public int RetryCount { get; set; } = 3;

    public bool UseDefaultCredentials { get; set; }

    /// <summary>User-Agent（null = 自动生成 AtomUpdate/{version} ({os}; {arch})）。</summary>
    public string? UserAgent { get; set; }
}

/// <summary>日志配置。</summary>
public sealed class LoggingOptions
{
    public LogLevel MinLevel { get; set; } = LogLevel.Information;

    public bool LogToFile { get; set; }

    public string? LogDirectory { get; set; }

    public int RetentionDays { get; set; } = 7;
}
