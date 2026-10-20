using AtomUpdate.Abstractions.Events;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Models;
using AtomUpdate.Logging;
using AtomUpdate.Policy;
using AtomUpdate.Tests.Sdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomUpdate.Tests.Sdk;

/// <summary>策略缓存与四层合并（04 §3.8）。</summary>
public class PolicyProviderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    private AtomUpdateOptions Options() => new()
    {
        ServerUrl = "http://localhost:5062",
        ProjectKey = "test",
        AppId = "com.test.app",
        CurrentVersion = "1.0.0",
        Cache = new CacheOptions { CacheDirectory = _tempRoot }
    };

    [Fact]
    public async Task NoServerPolicy_ReturnsLocalDefaults()
    {
        var options = Options();
        options.Polling.Interval = TimeSpan.FromMinutes(30);
        var provider = CreateProvider(options);

        var policy = await provider.GetCurrentAsync();

        Assert.Equal(0, policy.Version);
        Assert.Equal(30, policy.PollingIntervalMinutes);
        Assert.Equal(options.Install.Mode, policy.InstallMode);
        Assert.False(policy.DownloadOnStartup); // 下载触发开关默认关（模式由本地配置决定）
    }

    [Fact]
    public async Task ApplyServerPolicy_Merges_And_RaisesChanged()
    {
        var options = Options();
        var provider = CreateProvider(options);
        PolicyChangedEventArgs? received = null;
        provider.PolicyChanged += (_, e) => received = e;

        var server = new UpdatePolicy
        {
            Version = 3,
            PollingEnabled = true,
            PollingIntervalMinutes = 120,
            PollingJitterMinutes = 5,
            DownloadOnStartup = true,
            DownloadOnPolling = true,
            DownloadOnManualCheck = true,
            DownloadOnForceUpdate = true,
            DownloadOnNetworkRecovery = false,
            DownloadOnResume = false,
            InstallMode = Core.Enums.InstallMode.Immediate,
            AllowForceUpdate = true,
            AllowDowngrade = false,
            AllowPostpone = true,
            MaxPostponeCount = 5,
            AllowMeteredNetwork = true,
            MaxDownloadSpeedKBps = 1024,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var merged = await provider.ApplyServerPolicyAsync(server);

        Assert.Equal(3, merged.Version);
        Assert.Equal(120, merged.PollingIntervalMinutes);
        Assert.True(merged.DownloadOnPolling);
        Assert.NotNull(received);
        Assert.Equal(3, received!.NewPolicy.Version);
        Assert.Equal(0, received.OldPolicy!.Version);
    }

    [Fact]
    public async Task ApplySameVersion_NoEvent()
    {
        var options = Options();
        var provider = CreateProvider(options);
        var server = ServerPolicy(3);
        await provider.ApplyServerPolicyAsync(server); // 首次下发触发变更

        var raised = 0;
        provider.PolicyChanged += (_, _) => raised++;
        await provider.ApplyServerPolicyAsync(server); // 重复下发同版本同 UpdatedAt

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task CachePersistence_SurvivesNewProvider()
    {
        var options = Options();
        var provider = CreateProvider(options);
        await provider.SaveCacheAsync(new CachedPolicy
        {
            Policy = ServerPolicy(7),
            PolicyVersion = 7,
            ETag = "\"e7\"",
            LatestVersion = "9.9.9",
            LastCheckedAt = DateTimeOffset.UtcNow
        });

        var second = CreateProvider(options);
        var policy = await second.GetCurrentAsync();
        var cached = await second.GetCachedAsync();

        Assert.Equal(7, policy.Version); // 上次下发覆盖本地默认
        Assert.NotNull(cached);
        Assert.Equal("\"e7\"", cached!.ETag);
    }

    [Fact]
    public async Task CacheExpiration_Expired_ReturnsNull()
    {
        var options = Options();
        options.Cache.CacheExpiration = TimeSpan.FromMinutes(10);
        var provider = CreateProvider(options);
        await provider.SaveCacheAsync(new CachedPolicy
        {
            Policy = ServerPolicy(7),
            PolicyVersion = 7,
            LastCheckedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(1)
        });

        Assert.Null(await CreateProvider(options).GetCachedAsync());
    }

    [Fact]
    public async Task DisabledCache_NoPersistence()
    {
        var options = Options();
        options.Cache.EnablePolicyCache = false;
        var provider = CreateProvider(options);
        await provider.SaveCacheAsync(new CachedPolicy { PolicyVersion = 9, LastCheckedAt = DateTimeOffset.UtcNow });

        Assert.Null(await CreateProvider(options).GetCachedAsync());
    }

    private static PolicyProvider CreateProvider(AtomUpdateOptions options) =>
        new(options, new PolicyCache(options.AppId, options),
            new ExtensionsUpdateLogger(NullLoggerFactory.Instance));

    private static UpdatePolicy ServerPolicy(int version) => new()
    {
        Version = version,
        PollingEnabled = true,
        PollingIntervalMinutes = 60,
        PollingJitterMinutes = 5,
        DownloadOnStartup = false,
        DownloadOnPolling = true,
        DownloadOnManualCheck = false,
        DownloadOnForceUpdate = true,
        DownloadOnNetworkRecovery = false,
        DownloadOnResume = false,
        InstallMode = Core.Enums.InstallMode.OnRestart,
        AllowForceUpdate = false,
        AllowDowngrade = false,
        AllowPostpone = true,
        MaxPostponeCount = 3,
        AllowMeteredNetwork = false,
        MaxDownloadSpeedKBps = 0,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
