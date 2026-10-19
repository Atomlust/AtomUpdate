using AtomUpdate.Abstractions.Events;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Models;

namespace AtomUpdate.Policy;

/// <summary>策略合并（04 §3.8）：内置默认 → 本地配置 → 服务端上次下发 → 服务端本次下发。服务端策略只覆盖策略类字段，永远触不到身份与校验开关。</summary>
public sealed class PolicyProvider(
    AtomUpdateOptions options,
    PolicyCache cache,
    IUpdateLogger logger) : IPolicyProvider
{
    private UpdatePolicy? _current;

    public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;

    public async ValueTask<UpdatePolicy> GetCurrentAsync(CancellationToken ct = default)
    {
        if (_current is not null)
        {
            return _current;
        }

        var cached = options.Cache.EnablePolicyCache ? await cache.LoadAsync(ct) : null;
        _current = cached?.Policy is not null ? Merge(LocalPolicy(), cached.Policy) : LocalPolicy();
        return _current;
    }

    public async Task<UpdatePolicy> ApplyServerPolicyAsync(UpdatePolicy serverPolicy, CancellationToken ct = default)
    {
        var old = _current ?? await GetCurrentAsync(ct);
        var merged = Merge(old, serverPolicy);

        if (PolicyEquals(old, merged))
        {
            return merged;
        }

        _current = merged;
        logger.Information($"策略已更新至 v{merged.Version}");
        PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(old, merged));
        return merged;
    }

    public async ValueTask<CachedPolicy?> GetCachedAsync(CancellationToken ct = default) =>
        options.Cache.EnablePolicyCache ? await cache.LoadAsync(ct) : null;

    /// <summary>检查成功后持久化缓存（策略/版本/ETag/lastCheckedAt）。</summary>
    public async ValueTask SaveCacheAsync(CachedPolicy cached, CancellationToken ct = default)
    {
        if (options.Cache.EnablePolicyCache)
        {
            await cache.SaveAsync(cached, ct);
        }
    }

    /// <summary>内置默认 → 本地配置映射；下载触发开关默认全关（是否自动下载由服务端策略决定），模式（前台/静默）由本地 Download 配置决定。</summary>
    public UpdatePolicy LocalPolicy() => new()
    {
        Version = 0,
        PollingEnabled = options.Polling.Enabled,
        PollingIntervalMinutes = (int)Math.Ceiling(options.Polling.Interval.TotalMinutes),
        PollingJitterMinutes = options.Polling.JitterMinutes,
        DownloadOnStartup = false,
        DownloadOnPolling = false,
        DownloadOnManualCheck = false,
        DownloadOnForceUpdate = true,
        DownloadOnNetworkRecovery = false,
        DownloadOnResume = false,
        InstallMode = options.Install.Mode,
        AllowForceUpdate = false,
        AllowDowngrade = options.Install.AllowDowngrade,
        AllowPostpone = options.Install.AllowPostpone,
        MaxPostponeCount = options.Install.MaxPostponeCount,
        AllowMeteredNetwork = options.Download.AllowMeteredNetwork,
        MaxDownloadSpeedKBps = options.Download.MaxDownloadSpeedKBps,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    internal static UpdatePolicy Merge(UpdatePolicy baseline, UpdatePolicy overlay) => new()
    {
        Version = overlay.Version,
        PollingEnabled = overlay.PollingEnabled,
        PollingIntervalMinutes = overlay.PollingIntervalMinutes,
        PollingJitterMinutes = overlay.PollingJitterMinutes,
        DownloadOnStartup = overlay.DownloadOnStartup,
        DownloadOnPolling = overlay.DownloadOnPolling,
        DownloadOnManualCheck = overlay.DownloadOnManualCheck,
        DownloadOnForceUpdate = overlay.DownloadOnForceUpdate,
        DownloadOnNetworkRecovery = overlay.DownloadOnNetworkRecovery,
        DownloadOnResume = overlay.DownloadOnResume,
        InstallMode = overlay.InstallMode,
        AllowForceUpdate = overlay.AllowForceUpdate,
        AllowDowngrade = overlay.AllowDowngrade,
        AllowPostpone = overlay.AllowPostpone,
        MaxPostponeCount = overlay.MaxPostponeCount,
        AllowMeteredNetwork = overlay.AllowMeteredNetwork,
        MaxDownloadSpeedKBps = overlay.MaxDownloadSpeedKBps,
        UpdatedAt = overlay.UpdatedAt
    };

    private static bool PolicyEquals(UpdatePolicy? left, UpdatePolicy right) =>
        left is not null && left.Version == right.Version && left.UpdatedAt == right.UpdatedAt;
}
