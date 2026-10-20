using System.Text.Json;
using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Events;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Install;
using AtomUpdate.Internal;
using AtomUpdate.State;

namespace AtomUpdate;

/// <summary>SDK 门面（04 §3.2~§3.6）：状态机 + 事件（后台线程触发）+ 组合编排。</summary>
public sealed class AtomUpdater(
    AtomUpdateOptions options,
    IUpdateChecker checker,
    IUpdateDownloader downloader,
    IUpdateInstaller installer,
    IPolicyProvider policyProvider,
    IUpdateNotifier notifier,
    IUpdateReporter reporter,
    IStateStore stateStore,
    IPlatformInfo platformInfo,
    IUpdateLogger logger) : IAtomUpdater, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CheckPayload? _lastPayload;
    private string? _downloadedPackagePath;

    public event EventHandler<CheckCompletedEventArgs>? CheckCompleted;
    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;
    public event EventHandler<DownloadStartedEventArgs>? DownloadStarted;
    public event EventHandler<DownloadProgressChangedEventArgs>? DownloadProgressChanged;
    public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;
    public event EventHandler<DownloadFailedEventArgs>? DownloadFailed;
    public event EventHandler<UpdateReadyEventArgs>? UpdateReady;
    public event EventHandler<InstallStartedEventArgs>? InstallStarted;
    public event EventHandler<InstallCompletedEventArgs>? InstallCompleted;
    public event EventHandler<InstallFailedEventArgs>? InstallFailed;
    public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;

    private UpdateState _state = UpdateState.Idle;

    public UpdateState State => _state;

    public UpdateInfo? CurrentUpdate { get; private set; }

    /// <summary>推迟状态（截止时间与已推迟次数，具体类附加成员）。</summary>
    public DateTimeOffset? PostponedUntil { get; private set; }

    public int PostponeCount { get; private set; }

    /// <summary>已就绪的安装包路径（ReadyToInstall 状态下非空）。</summary>
    public string? DownloadedPackagePath => _downloadedPackagePath;

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    private void SetState(UpdateState newState)
    {
        if (_state == newState)
        {
            return;
        }

        var old = _state;
        _state = newState;
        StateChanged?.Invoke(this, new StateChangedEventArgs(old, newState));
    }

    private async Task RestorePostponeStateAsync()
    {
        var untilValue = await stateStore.GetStringAsync(StateKeys.PostponeUntil);
        if (untilValue is not null && DateTimeOffset.TryParse(untilValue, out var until))
        {
            PostponedUntil = until;
        }

        var countValue = await stateStore.GetStringAsync(StateKeys.PostponeCount);
        if (countValue is not null && int.TryParse(countValue, out var count))
        {
            PostponeCount = count;
        }
    }

    public async Task<CheckResult> CheckAsync(UpdateTrigger trigger = UpdateTrigger.Manual,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await CheckCoreAsync(trigger, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<CheckResult> CheckManuallyAsync(CancellationToken ct = default) =>
        CheckAsync(UpdateTrigger.Manual, ct);

    private async Task<CheckResult> CheckCoreAsync(UpdateTrigger trigger, CancellationToken ct)
    {
        SetState(UpdateState.Checking);
        await RestorePostponeStateAsync();

        var context = await BuildCheckContextAsync(trigger, ct);
        var operation = await checker.CheckAsync(context, ct);
        var result = CheckResult.From(operation);

        if (operation.Success)
        {
            _lastPayload = operation.Data;
            if (operation.Data!.ServerPolicy is not null)
            {
                // 策略合并（四层），变更时转发 PolicyChanged 事件
                var oldPolicy = await policyProvider.GetCurrentAsync(ct);
                var merged = await policyProvider.ApplyServerPolicyAsync(operation.Data.ServerPolicy, ct);
                if (merged.Version != oldPolicy.Version)
                {
                    PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(oldPolicy, merged));
                }
            }

            if (operation.Data.HasUpdate && operation.Data.Update is not null)
            {
                CurrentUpdate = operation.Data.Update;
                SetState(UpdateState.UpdateAvailable);
                UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(operation.Data.Update));
                await NotifyAsync(NotificationType.UpdateAvailable, operation.Data.Update, ct);
            }
            else
            {
                CurrentUpdate = null;
                SetState(UpdateState.NoUpdate);
            }
        }
        else
        {
            SetState(UpdateState.Failed);
        }

        RaiseCheckCompleted(result);
        await ReportCheckAsync(trigger, result, ct);
        return result;
    }

    private void RaiseCheckCompleted(CheckResult result) =>
        CheckCompleted?.Invoke(this, new CheckCompletedEventArgs(result));

    public async Task<DownloadResult> DownloadAsync(DownloadMode? mode = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await DownloadCoreAsync(mode ?? DownloadMode.Foreground, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DownloadResult> DownloadCoreAsync(DownloadMode mode, CancellationToken ct)
    {
        if (CurrentUpdate is null)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadFailed, "当前无可用更新（请先检查）");
        }

        var package = CurrentUpdate.ResolvePackage();
        if (package is null)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadFailed, "更新响应缺少包资产");
        }

        SetState(mode == DownloadMode.Foreground
            ? UpdateState.ForegroundDownloading
            : UpdateState.SilentDownloading);
        DownloadStarted?.Invoke(this, new DownloadStartedEventArgs(CurrentUpdate));

        var policy = await policyProvider.GetCurrentAsync(ct);
        var speedLimitKbps = Math.Max(policy.MaxDownloadSpeedKBps, options.Download.MaxDownloadSpeedKBps);
        var context = new DownloadContext
        {
            Update = CurrentUpdate,
            Package = new PackageAssetRef
            {
                Url = package.Url,
                Sha256 = package.Sha256,
                PackageType = package.PackageType.ToString().ToLowerInvariant(),
                Signature = package.Signature,
                KeyId = package.KeyId,
                Size = package.Size
            },
            Mode = mode,
            DestinationDirectory = options.Download.TempDirectory
                ?? System.IO.Path.Combine(Internal.PlatformPaths.ResolveCacheRoot(options.Cache.CacheDirectory),
                    Internal.PlatformPaths.Sanitize(options.AppId), "downloads"),
            MaxSpeedBytesPerSecond = speedLimitKbps * 1024,
            StallTimeoutSeconds = options.Download.StallTimeoutSeconds
        };

        var downloadStopwatch = System.Diagnostics.Stopwatch.StartNew();
        // 同步转发进度：Progress<T> 会跨上下文投递，事件乱序会让 UI 百分比倒退
        var result = await downloader.DownloadAsync(context, new Internal.InlineProgress<DownloadProgress>(p =>
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedEventArgs(p))), ct);

        if (!result.Success)
        {
            SetState(UpdateState.Failed);
            DownloadFailed?.Invoke(this, new DownloadFailedEventArgs(result.ErrorCode, result.ErrorMessage,
                result.Exception));
            if (options.Notify.ShowOnDownloadFailed)
            {
                await NotifyAsync(NotificationType.DownloadFailed, CurrentUpdate, ct, result.ErrorMessage);
            }

            await ReportDownloadAsync(CurrentUpdate, success: false, downloadStopwatch.ElapsedMilliseconds,
                result.Bytes, result.ErrorCode, ct);
            return result;
        }

        _downloadedPackagePath = result.PackagePath;
        SetState(UpdateState.ReadyToInstall);
        DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs(CurrentUpdate, result.PackagePath!));
        UpdateReady?.Invoke(this, new UpdateReadyEventArgs(CurrentUpdate, result.PackagePath!));
        if (options.Notify.ShowOnReady)
        {
            await NotifyAsync(NotificationType.UpdateReady, CurrentUpdate, ct);
        }

        await ReportDownloadAsync(CurrentUpdate, success: true, downloadStopwatch.ElapsedMilliseconds,
            result.Bytes, null, ct);
        return result;
    }

    public async Task<CheckAndDownloadResult> CheckAndDownloadAsync(UpdateTrigger trigger,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var check = await CheckCoreAsync(trigger, ct);
            var download = DownloadResult.Ok("");

            if (check.Success && check.HasUpdate && CurrentUpdate is not null)
            {
                var policy = await policyProvider.GetCurrentAsync(ct);
                var shouldDownload = CurrentUpdate.ForceUpdate || ShouldAutoDownload(trigger, policy);
                if (shouldDownload)
                {
                    var mode = CurrentUpdate.ForceUpdate
                        ? DownloadMode.Foreground
                        : options.Download.GetMode(trigger);
                    download = await DownloadCoreAsync(mode, ct);
                }
            }

            return new CheckAndDownloadResult(check, download);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ShouldAutoDownload(UpdateTrigger trigger, UpdatePolicy policy) => trigger switch
    {
        UpdateTrigger.Startup => policy.DownloadOnStartup,
        UpdateTrigger.Polling => policy.DownloadOnPolling,
        UpdateTrigger.Manual => policy.DownloadOnManualCheck,
        UpdateTrigger.NetworkRecovery => policy.DownloadOnNetworkRecovery,
        UpdateTrigger.Resume => policy.DownloadOnResume,
        UpdateTrigger.Force => true,
        _ => false
    };

    public async Task<InstallResult> InstallAndRestartAsync(CancellationToken ct = default)
    {
        if (CurrentUpdate is null || _downloadedPackagePath is null)
        {
            return InstallResult.Fail(ErrorCodes.InstallFailed, "无可安装的更新包（请先完成检查与下载）");
        }

        var policy = await policyProvider.GetCurrentAsync(ct);

        // 降级保护（04 §3.7 Install.AllowDowngrade）
        if (options.Install.AllowDowngrade is false &&
            Version.TryParse(CurrentUpdate.LatestVersion, out var latest) &&
            Version.TryParse(options.CurrentVersion, out var current) &&
            latest < current)
        {
            return InstallResult.Fail(ErrorCodes.InstallFailed,
                $"拒绝降级安装：{options.CurrentVersion} → {CurrentUpdate.LatestVersion}");
        }

        SetState(UpdateState.InstallScheduled);
        InstallStarted?.Invoke(this, new InstallStartedEventArgs(CurrentUpdate));

        var result = await installer.InstallAsync(new InstallContext
        {
            PackagePath = _downloadedPackagePath,
            Update = CurrentUpdate,
            Mode = policy.InstallMode,
            RestartPath = Environment.ProcessPath ?? "",
            RestartArgs = options.Install.RestartArgs
        }, ct);

        if (!result.Success)
        {
            SetState(UpdateState.Failed);
            InstallFailed?.Invoke(this, new InstallFailedEventArgs(result.ErrorCode, result.ErrorMessage,
                result.Exception));
            await NotifyAsync(NotificationType.InstallFailed, CurrentUpdate, ct, result.ErrorMessage);
            return result;
        }

        SetState(UpdateState.RestartPending);
        InstallCompleted?.Invoke(this, new InstallCompletedEventArgs(CurrentUpdate));

        // 安装上报（失败自动落盘，更新器回执由状态文件补报）
        await reporter.ReportInstallAsync(new InstallReport
        {
            ProjectKey = options.ProjectKey,
            AppId = options.AppId,
            InstallationId = await stateStore.GetStringAsync(InstallationIdentity.InstallationIdKey, ct) ?? "",
            MachineFingerprint = options.Report.ReportMachineFingerprint
                ? await stateStore.GetStringAsync(InstallationIdentity.FingerprintKey, ct)
                : null,
            FromVersion = options.CurrentVersion,
            ToVersion = CurrentUpdate.LatestVersion,
            Stage = UpdateStage.Installing,
            InstallMode = policy.InstallMode
        }, ct);

        return result;
    }

    public void Postpone(TimeSpan duration)
    {
        if (CurrentUpdate is { ForceUpdate: true })
        {
            logger.Warning("强制更新不可推迟");
            return;
        }

        var policy = policyProvider.GetCurrentAsync().AsTask().GetAwaiter().GetResult();
        if (!policy.AllowPostpone || PostponeCount >= policy.MaxPostponeCount)
        {
            logger.Warning("推迟次数已用尽或被策略禁止");
            return;
        }

        PostponeCount++;
        PostponedUntil = DateTimeOffset.UtcNow + duration;
        SetState(UpdateState.Postponed);
        stateStore.SetStringAsync(StateKeys.PostponeCount, PostponeCount.ToString())
            .GetAwaiter().GetResult();
        stateStore.SetStringAsync(StateKeys.PostponeUntil, PostponedUntil.Value.ToString("O"))
            .GetAwaiter().GetResult();
    }

    /// <summary>推迟是否已超期/超次（进入强制路径）。</summary>
    public bool IsPostponeExhausted()
    {
        if (CurrentUpdate is { ForceUpdate: true })
        {
            return true;
        }

        if (PostponedUntil is { } until && DateTimeOffset.UtcNow > until)
        {
            return true;
        }

        var policy = policyProvider.GetCurrentAsync().AsTask().GetAwaiter().GetResult();
        return policy.AllowPostpone && PostponeCount >= policy.MaxPostponeCount;
    }

    private async Task<CheckContext> BuildCheckContextAsync(UpdateTrigger trigger, CancellationToken ct)
    {
        var cached = await policyProvider.GetCachedAsync(ct);
        return new CheckContext
        {
            Trigger = trigger,
            ProjectKey = options.ProjectKey,
            AppId = options.AppId,
            CurrentVersion = options.CurrentVersion,
            Channel = options.Channel,
            Platform = platformInfo.Platform,
            Arch = platformInfo.Arch,
            InstallationId = await InstallationIdentity.GetOrCreateInstallationIdAsync(stateStore, ct),
            PolicyVersion = cached?.PolicyVersion ?? 0,
            ETag = options.Cache.EnableETag ? cached?.ETag : null
        };
    }

    private async Task NotifyAsync(NotificationType type, UpdateInfo update, CancellationToken ct,
        string? error = null)
    {
        try
        {
            await notifier.NotifyAsync(new NotificationContext
            {
                Type = type,
                Version = update.LatestVersion,
                Title = options.Notify.TitleTemplate.Replace("{version}", update.LatestVersion),
                Body = error ?? options.Notify.BodyTemplate?.Replace("{version}", update.LatestVersion),
                ReleaseNotes = options.Notify.ShowReleaseNotes ? update.ReleaseNotes : null,
                ForceUpdate = update.ForceUpdate
            }, ct);
        }
        catch (Exception exception)
        {
            logger.Warning("通知呈现失败", exception);
        }
    }

    private async Task ReportCheckAsync(UpdateTrigger trigger, CheckResult result, CancellationToken ct)
    {
        if (!options.Report.Enabled || !options.Report.ReportInstallationId)
        {
            return;
        }

        try
        {
            await reporter.ReportCheckAsync(new CheckReport
            {
                ProjectKey = options.ProjectKey,
                AppId = options.AppId,
                InstallationId = await InstallationIdentity.GetOrCreateInstallationIdAsync(stateStore, ct),
                CurrentVersion = options.CurrentVersion,
                LatestVersion = result.Update?.LatestVersion,
                HasUpdate = result.HasUpdate,
                Trigger = trigger
            }, ct);
        }
        catch (Exception exception)
        {
            logger.Warning("检查上报失败", exception);
        }
    }

    private async Task ReportDownloadAsync(UpdateInfo update, bool success, long durationMs, long bytes,
        string? errorCode, CancellationToken ct)
    {
        if (!options.Report.Enabled)
        {
            return;
        }

        try
        {
            await reporter.ReportDownloadAsync(new DownloadReport
            {
                ProjectKey = options.ProjectKey,
                AppId = options.AppId,
                InstallationId = await InstallationIdentity.GetOrCreateInstallationIdAsync(stateStore, ct),
                FromVersion = options.CurrentVersion,
                ToVersion = update.LatestVersion,
                Kind = update.ResolvePackage()?.Type,
                Success = success,
                Bytes = bytes,
                DurationMs = durationMs,
                ErrorCode = errorCode
            }, ct);
        }
        catch (Exception exception)
        {
            logger.Warning("下载上报失败", exception);
        }
    }

    /// <summary>启动上报（04 §3.14：宿主启动时一次，活跃回执 + 冲刷补报队列；由 StartupReportService 触发）。</summary>
    public async Task<bool> ReportStartupAsync(CancellationToken ct = default)
    {
        if (!options.Report.Enabled)
        {
            return true;
        }

        // 更新器未回传的安装结果先补报并回写 reported=true，再做队列冲刷——避免同一事件被状态补报与队列各投一次
        await ReportUpdaterStateAsync(ct);

        if (!options.Report.ReportOnStartup)
        {
            return true;
        }

        return await reporter.ReportStartupAsync(new StartupReport
        {
            ProjectKey = options.ProjectKey,
            AppId = options.AppId,
            InstallationId = options.Report.ReportInstallationId
                ? await InstallationIdentity.GetOrCreateInstallationIdAsync(stateStore, ct)
                : "",
            MachineFingerprint = options.Report.ReportMachineFingerprint
                ? await InstallationIdentity.GetOrCreateFingerprintAsync(stateStore, options.AppId, ct)
                : null,
            Version = options.CurrentVersion
        }, ct);
    }

    /// <summary>更新器状态回读补报（04 §4.4/§5.3）：state/{appId}/updater-state.json 中 reported=false 时补报安装结果。</summary>
    private async Task ReportUpdaterStateAsync(CancellationToken ct)
    {
        var path = Path.Combine(PlatformPaths.ResolveStateRoot(),
            PlatformPaths.Sanitize(options.AppId), "updater-state.json");
        if (!File.Exists(path))
        {
            return;
        }

        UpdaterStateFile? state;
        try
        {
            state = JsonSerializer.Deserialize<UpdaterStateFile>(
                await File.ReadAllTextAsync(path, ct), UpdateReporterJson.Options);
        }
        catch (Exception exception)
        {
            logger.Warning($"更新器状态文件读取失败：{path}", exception);
            return;
        }

        if (state is null || state.Reported)
        {
            return;
        }

        bool delivered;
        try
        {
            delivered = await reporter.ReportInstallAsync(new InstallReport
            {
                ProjectKey = state.ProjectKey ?? options.ProjectKey,
                AppId = state.AppId ?? options.AppId,
                InstallationId = state.InstallationId ?? await InstallationIdentity
                    .GetOrCreateInstallationIdAsync(stateStore, ct),
                MachineFingerprint = state.MachineFingerprint,
                FromVersion = state.FromVersion,
                ToVersion = state.ToVersion ?? "",
                Stage = ParseStage(state.Stage),
                InstallMode = ParseInstallMode(state.InstallMode),
                Elevated = state.Elevated,
                ElevationRejected = state.ElevationRejected,
                ErrorCode = state.ErrorCode,
                ErrorMessage = state.ErrorMessage,
                LogPath = state.LogPath,
                Timestamp = state.Timestamp
            }, ct);
        }
        catch (Exception exception)
        {
            logger.Warning("更新器状态补报失败", exception);
            return;
        }

        // 已送达或已入本地补报队列，均视为交付责任转移；否则保留 reported=false 等下次启动重试
        if (!delivered && !(options.Report.FallbackToLocal && options.Cache.EnableOfflineReport))
        {
            logger.Warning("更新器状态补报未送达且离线队列未启用，保留 reported=false");
            return;
        }

        try
        {
            state.Reported = true;
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(state, UpdateReporterJson.Options), ct);
            logger.Information($"更新器状态补报完成：{state.Stage}/{state.Result ?? "-"}");
        }
        catch (Exception exception)
        {
            logger.Warning($"更新器状态回写失败：{path}", exception);
        }
    }

    private static UpdateStage ParseStage(string? stage) => stage?.ToLowerInvariant() switch
    {
        "verifying" => UpdateStage.Verifying,
        "installing" => UpdateStage.Installing,
        "completed" => UpdateStage.Completed,
        "failed" => UpdateStage.Failed,
        "rolledback" => UpdateStage.RolledBack,
        _ => UpdateStage.Starting
    };

    private static InstallMode ParseInstallMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "manual" => InstallMode.Manual,
        "immediate" => InstallMode.Immediate,
        _ => InstallMode.OnRestart
    };

    public void Dispose() => _gate.Dispose();
}
