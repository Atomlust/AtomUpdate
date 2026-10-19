using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Events;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;

namespace AtomUpdate.Abstractions.Interfaces;

/// <summary>SDK 门面（04 §3.2）。所有公开方法不阻塞主线程、可取消、返回结果对象。</summary>
public interface IAtomUpdater
{
    UpdateState State { get; }

    /// <summary>当前可用更新（有更新且未安装完成时非空）。</summary>
    UpdateInfo? CurrentUpdate { get; }

    Task<CheckResult> CheckAsync(UpdateTrigger trigger = UpdateTrigger.Manual, CancellationToken ct = default);

    Task<DownloadResult> DownloadAsync(DownloadMode? mode = null, CancellationToken ct = default);

    Task<CheckAndDownloadResult> CheckAndDownloadAsync(UpdateTrigger trigger, CancellationToken ct = default);

    /// <summary>调度安装并重启：onRestart 模式写入调度状态后由宿主退出触发；immediate 立即启动更新器。</summary>
    Task<InstallResult> InstallAndRestartAsync(CancellationToken ct = default);

    /// <summary>推迟更新（强制更新不可推迟）。超期/超次进入强制路径。</summary>
    void Postpone(TimeSpan duration);

    Task<CheckResult> CheckManuallyAsync(CancellationToken ct = default);

    /// <summary>启动上报：活跃回执 + 冲刷 pending-reports 补报队列（04 §3.14）。SDK 的启动服务自动调用，宿主也可手动补一次。</summary>
    Task<bool> ReportStartupAsync(CancellationToken ct = default);

    // 事件统一在后台线程触发（04 §3.4）
    event EventHandler<CheckCompletedEventArgs>? CheckCompleted;
    event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;
    event EventHandler<DownloadStartedEventArgs>? DownloadStarted;
    event EventHandler<DownloadProgressChangedEventArgs>? DownloadProgressChanged;
    event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;
    event EventHandler<DownloadFailedEventArgs>? DownloadFailed;
    event EventHandler<UpdateReadyEventArgs>? UpdateReady;
    event EventHandler<InstallStartedEventArgs>? InstallStarted;
    event EventHandler<InstallCompletedEventArgs>? InstallCompleted;
    event EventHandler<InstallFailedEventArgs>? InstallFailed;
    event EventHandler<PolicyChangedEventArgs>? PolicyChanged;
    event EventHandler<StateChangedEventArgs>? StateChanged;
}

/// <summary>检查更新（04 §3.3）。</summary>
public interface IUpdateChecker
{
    Task<OperationResult<CheckPayload>> CheckAsync(CheckContext context, CancellationToken ct = default);
}

/// <summary>下载更新包（04 §3.3）。</summary>
public interface IUpdateDownloader
{
    Task<DownloadResult> DownloadAsync(DownloadContext context, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default);
}

/// <summary>调度安装（04 §3.3）：准备更新器参数、启动更新器、主程序退出。</summary>
public interface IUpdateInstaller
{
    Task<InstallResult> InstallAsync(InstallContext context, CancellationToken ct = default);
}

/// <summary>策略提供与合并（04 §3.3/§3.8）。</summary>
public interface IPolicyProvider
{
    /// <summary>当前生效策略（内置默认 → 本地配置 → 上次服务端下发 合并结果）。</summary>
    ValueTask<UpdatePolicy> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>应用服务端下发策略（合并 + 持久化），返回合并结果。</summary>
    Task<UpdatePolicy> ApplyServerPolicyAsync(UpdatePolicy serverPolicy, CancellationToken ct = default);

    /// <summary>最近一次缓存的策略元数据。</summary>
    ValueTask<CachedPolicy?> GetCachedAsync(CancellationToken ct = default);

    event EventHandler<PolicyChangedEventArgs>? PolicyChanged;
}

/// <summary>通知（04 §3.13）。MVP 提供 Console 实现与 Auto 回落。</summary>
public interface IUpdateNotifier
{
    Task NotifyAsync(NotificationContext context, CancellationToken ct = default);
}

/// <summary>状态上报（04 §3.3）：失败自动落盘 pending-reports，启动时补报。</summary>
public interface IUpdateReporter
{
    Task<bool> ReportCheckAsync(CheckReport report, CancellationToken ct = default);

    Task<bool> ReportDownloadAsync(DownloadReport report, CancellationToken ct = default);

    /// <summary>安装上报；report 参数可空（此时仅补报队列）。</summary>
    Task<bool> ReportInstallAsync(InstallReport? report, CancellationToken ct = default);

    /// <summary>启动上报 + 冲刷 pending-reports 补报队列。</summary>
    Task<bool> ReportStartupAsync(StartupReport report, CancellationToken ct = default);

    Task<bool> ReportBatchAsync(IReadOnlyList<object> events, CancellationToken ct = default);
}

/// <summary>键值持久化抽象（04 §3.3），默认文件实现，按 appId 隔离目录。</summary>
public interface IStateStore
{
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);

    Task SetStringAsync(string key, string value, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>日志抽象（04 §3.3），默认桥接 Microsoft.Extensions.Logging。</summary>
public interface IUpdateLogger
{
    void Debug(string message);

    void Information(string message);

    void Warning(string message, Exception? exception = null);

    void Error(string message, Exception? exception = null);
}

/// <summary>平台探测（04 §3.11）。</summary>
public interface IPlatformInfo
{
    UpdatePlatform Platform { get; }

    UpdateArch Arch { get; }

    string OsVersion { get; }

    string RuntimeIdentifier { get; }
}
