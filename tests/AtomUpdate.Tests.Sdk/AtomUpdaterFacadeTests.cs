using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Events;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Enums;
using AtomUpdate.Logging;
using AtomUpdate.Notify;
using AtomUpdate.Policy;
using AtomUpdate.State;
using AtomUpdate.Tests.Sdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomUpdate.Tests.Sdk;

/// <summary>门面状态机与事件（04 §3.2~§3.6，W2 出口：全状态机路径与事件单测）。</summary>
public class AtomUpdaterFacadeTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    private AtomUpdateOptions Options() => new()
    {
        ServerUrl = "http://localhost:5062",
        ProjectKey = "test",
        AppId = "com.test.app",
        CurrentVersion = "1.0.0",
        Cache = new CacheOptions { CacheDirectory = _tempRoot },
        Download = new DownloadOptions { TempDirectory = Path.Combine(_tempRoot, "downloads") }
    };

    private sealed class TestScope
    {
        public required AtomUpdateOptions Options { get; init; }
        public required AtomUpdater Updater { get; init; }
        public required FakeChecker Checker { get; init; }
        public required FakeDownloader Downloader { get; init; }
        public required FakeInstaller Installer { get; init; }
        public required FakeNotifier Notifier { get; init; }
        public required FakeReporter Reporter { get; init; }
        public required PolicyProvider Policies { get; init; }

        public List<UpdateState> States { get; } = [];

        public List<string> Events { get; } = [];

        public static TestScope Create(AtomUpdateOptions options, CheckPayload? checkPayload = null)
        {
            var checker = new FakeChecker(checkPayload is null
                ? OperationResult<CheckPayload>.Ok(new CheckPayload { HasUpdate = false })
                : OperationResult<CheckPayload>.Ok(checkPayload));
            var downloader = new FakeDownloader();
            var installer = new FakeInstaller();
            var notifier = new FakeNotifier();
            var reporter = new FakeReporter();
            var policies = new PolicyProvider(options, new PolicyCache(options.AppId, options),
                new ExtensionsUpdateLogger(NullLoggerFactory.Instance));
            var stateStore = new FileStateStore(options.AppId, Path.Combine(options.Cache.CacheDirectory!, "state"));
            var logger = new ExtensionsUpdateLogger(NullLoggerFactory.Instance);
            var updater = new AtomUpdater(options, checker, downloader, installer, policies, notifier, reporter,
                stateStore, new Platform.PlatformInfo(), logger);

            var scope = new TestScope
            {
                Options = options,
                Updater = updater,
                Checker = checker,
                Downloader = downloader,
                Installer = installer,
                Notifier = notifier,
                Reporter = reporter,
                Policies = policies
            };
            updater.StateChanged += (_, e) => scope.States.Add(e.NewState);
            updater.CheckCompleted += (_, _) => scope.Events.Add(nameof(IAtomUpdater.CheckCompleted));
            updater.UpdateAvailable += (_, _) => scope.Events.Add(nameof(IAtomUpdater.UpdateAvailable));
            updater.DownloadStarted += (_, _) => scope.Events.Add(nameof(IAtomUpdater.DownloadStarted));
            updater.DownloadCompleted += (_, _) => scope.Events.Add(nameof(IAtomUpdater.DownloadCompleted));
            updater.DownloadFailed += (_, _) => scope.Events.Add(nameof(IAtomUpdater.DownloadFailed));
            updater.UpdateReady += (_, _) => scope.Events.Add(nameof(IAtomUpdater.UpdateReady));
            updater.InstallStarted += (_, _) => scope.Events.Add(nameof(IAtomUpdater.InstallStarted));
            return scope;
        }
    }

    private static CheckPayload UpdatePayload(bool forceUpdate = false) => new()
    {
        HasUpdate = true,
        ETag = "\"v2\"",
        Update = new UpdateInfo
        {
            LatestVersion = "2.0.0",
            ForceUpdate = forceUpdate,
            FullPackage = new Core.Models.PackageAsset
            {
                PackageType = PackageType.Zip,
                Arch = UpdateArch.X64,
                Url = "https://cdn.test/app-2.0.0.zip",
                Sha256 = new string('a', 64)
            }
        }
    };

    [Fact]
    public async Task Check_NoUpdate_StateFlow()
    {
        var scope = TestScope.Create(Options());

        var result = await scope.Updater.CheckAsync(UpdateTrigger.Startup);

        Assert.True(result.Success);
        Assert.False(result.HasUpdate);
        Assert.Equal(UpdateState.NoUpdate, scope.Updater.State);
        Assert.Equal([UpdateState.Checking, UpdateState.NoUpdate], scope.States);
        Assert.Equal(["CheckCompleted"], scope.Events);
    }

    [Fact]
    public async Task Check_HasUpdate_RaisesAvailable_And_Notifies()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());

        var result = await scope.Updater.CheckAsync(UpdateTrigger.Manual);

        Assert.True(result.Success);
        Assert.True(result.HasUpdate);
        Assert.NotNull(scope.Updater.CurrentUpdate);
        Assert.Equal(UpdateState.UpdateAvailable, scope.Updater.State);
        Assert.Contains("UpdateAvailable", scope.Events);
        Assert.Equal(NotificationType.UpdateAvailable, scope.Notifier.Notifications.Single().Type);
    }

    [Fact]
    public async Task Check_Failure_StateFailed()
    {
        var options = Options();
        var checker = new FakeChecker(OperationResult<CheckPayload>.Fail("SERVER_ERROR", "HTTP 500"));
        var updater = BuildUpdater(options, checker);
        var states = new List<UpdateState>();
        updater.StateChanged += (_, e) => states.Add(e.NewState);

        var result = await updater.CheckAsync();

        Assert.False(result.Success);
        Assert.Equal("SERVER_ERROR", result.ErrorCode);
        Assert.Equal(UpdateState.Failed, updater.State);
        Assert.Contains(UpdateState.Failed, states);
    }

    [Fact]
    public async Task CheckAndDownload_ForceUpdate_AutoForegroundDownload()
    {
        var scope = TestScope.Create(Options(), UpdatePayload(forceUpdate: true));
        scope.Downloader.Respond = _ => DownloadResult.Ok(@"C:\tmp\pkg.zip");

        var result = await scope.Updater.CheckAndDownloadAsync(UpdateTrigger.Force);

        Assert.True(result.Success);
        Assert.Single(scope.Downloader.Calls);
        Assert.Equal(DownloadMode.Foreground, scope.Downloader.Calls[0].Mode);
        Assert.Equal(UpdateState.ReadyToInstall, scope.Updater.State);
        Assert.Contains("UpdateReady", scope.Events);
        // 通知序列：检查发现更新 → 下载就绪
        Assert.Equal(NotificationType.UpdateAvailable, scope.Notifier.Notifications[0].Type);
        Assert.Equal(NotificationType.UpdateReady, scope.Notifier.Notifications[^1].Type);
    }

    [Fact]
    public async Task CheckAndDownload_PolicyDisallowsDownload_NoDownload()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());

        var result = await scope.Updater.CheckAndDownloadAsync(UpdateTrigger.Polling);

        Assert.True(result.Success);
        Assert.Empty(scope.Downloader.Calls); // DownloadOnPolling 默认 false
        Assert.Equal(UpdateState.UpdateAvailable, scope.Updater.State);
    }

    [Fact]
    public async Task CheckAndDownload_PolicyAllowsDownload_SilentMode()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());
        scope.Downloader.Respond = _ => DownloadResult.Ok(@"C:\tmp\pkg.zip");
        await scope.Policies.ApplyServerPolicyAsync(PolicyWith(downloadOnPolling: true));

        var result = await scope.Updater.CheckAndDownloadAsync(UpdateTrigger.Polling);

        Assert.True(result.Success);
        Assert.Single(scope.Downloader.Calls);
        Assert.Equal(DownloadMode.Silent, scope.Downloader.Calls[0].Mode); // Polling → 静默
    }

    [Fact]
    public async Task DownloadFailure_RaisesFailed_And_Notifies()
    {
        var options = Options();
        options.Notify.ShowOnDownloadFailed = true;
        var scope = TestScope.Create(options, UpdatePayload());
        scope.Downloader.Respond = _ => DownloadResult.Fail("DOWNLOAD_FAILED", "磁盘已满");
        await scope.Updater.CheckAsync(UpdateTrigger.Manual);

        var download = await scope.Updater.DownloadAsync();

        Assert.False(download.Success);
        Assert.Equal(UpdateState.Failed, scope.Updater.State);
        Assert.Contains("DownloadFailed", scope.Events);
        Assert.Equal(NotificationType.DownloadFailed, scope.Notifier.Notifications.Last().Type);
    }

    [Fact]
    public async Task Install_WithoutPackage_Fails()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());

        var result = await scope.Updater.InstallAndRestartAsync();

        Assert.False(result.Success);
        Assert.Empty(scope.Installer.Calls);
    }

    [Fact]
    public async Task Install_AfterDownload_SubmitsInstaller()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());
        scope.Downloader.Respond = _ => DownloadResult.Ok(@"C:\tmp\pkg.zip");
        await scope.Updater.CheckAsync(UpdateTrigger.Manual);
        await scope.Updater.DownloadAsync();

        var result = await scope.Updater.InstallAndRestartAsync();

        Assert.True(result.Scheduled);
        Assert.Single(scope.Installer.Calls);
        Assert.Equal(@"C:\tmp\pkg.zip", scope.Installer.Calls[0].PackagePath);
        Assert.Equal(UpdateState.RestartPending, scope.Updater.State);
        Assert.Contains("InstallStarted", scope.Events);
    }

    [Fact]
    public async Task Install_DowngradeBlocked()
    {
        var options = Options();
        options.CurrentVersion = "3.0.0"; // 本地版本更高 → 拒绝降级
        var scope = TestScope.Create(options, UpdatePayload());
        scope.Downloader.Respond = _ => DownloadResult.Ok(@"C:\tmp\pkg.zip");
        await scope.Updater.CheckAsync(UpdateTrigger.Manual);
        await scope.Updater.DownloadAsync();

        var result = await scope.Updater.InstallAndRestartAsync();

        Assert.False(result.Success);
        Assert.Contains("降级", result.ErrorMessage);
    }

    [Fact]
    public async Task Postpone_ForceUpdate_Ignored()
    {
        var scope = TestScope.Create(Options(), UpdatePayload(forceUpdate: true));
        await scope.Updater.CheckAsync(UpdateTrigger.Manual);

        scope.Updater.Postpone(TimeSpan.FromHours(1));

        Assert.Null(scope.Updater.PostponedUntil);
        Assert.NotEqual(UpdateState.Postponed, scope.Updater.State);
    }

    [Fact]
    public async Task Postpone_CountsUp_ExhaustsAtLimit()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());
        await scope.Policies.ApplyServerPolicyAsync(PolicyWith(maxPostponeCount: 2));

        scope.Updater.Postpone(TimeSpan.FromMinutes(10));
        scope.Updater.Postpone(TimeSpan.FromMinutes(10));
        scope.Updater.Postpone(TimeSpan.FromMinutes(10));

        Assert.Equal(2, scope.Updater.PostponeCount); // 第三次被拒
        Assert.True(scope.Updater.IsPostponeExhausted());
    }

    [Fact]
    public async Task ServerPolicy_Applied_And_EventForwarded()
    {
        var scope = TestScope.Create(Options(), new CheckPayload
        {
            HasUpdate = false,
            ServerPolicy = PolicyWith(downloadOnPolling: true),
            PolicyVersion = 4
        });
        PolicyChangedEventArgs? changed = null;
        scope.Updater.PolicyChanged += (_, e) => changed = e;

        await scope.Updater.CheckAsync();

        Assert.Equal(4, (await scope.Policies.GetCurrentAsync()).Version);
        Assert.NotNull(changed);
        Assert.Equal(4, changed!.NewPolicy.Version);
    }

    /// <summary>进度事件同步按序送达（E2E 回归：Progress&lt;T&gt; 跨上下文投递会让 UI 看到百分比乱序/倒退）。</summary>
    [Fact]
    public async Task DownloadProgress_DeliveredSynchronouslyInOrder()
    {
        var scope = TestScope.Create(Options(), UpdatePayload());
        var observed = new List<long>();
        scope.Updater.DownloadProgressChanged += (_, e) => observed.Add(e.Progress.BytesReceived);
        scope.Downloader.Respond = _ => DownloadResult.Ok(@"C:\tmp\pkg.zip");
        for (var i = 1; i <= 8; i++)
        {
            scope.Downloader.ProgressReports.Add(new DownloadProgress { BytesReceived = i * 1024 });
        }

        // Report 返回时事件必须已投递完毕（不排队到别的上下文）
        scope.Downloader.AfterEachReport = expected => Assert.Equal(expected, observed.Count);
        await scope.Updater.CheckAsync(UpdateTrigger.Manual);

        var download = await scope.Updater.DownloadAsync();

        Assert.True(download.Success);
        Assert.Equal([1024L, 2048L, 3072L, 4096L, 5120L, 6144L, 7168L, 8192L], observed);
    }

    private static AtomUpdater BuildUpdater(AtomUpdateOptions options, FakeChecker checker)
    {
        var logger = new ExtensionsUpdateLogger(NullLoggerFactory.Instance);
        var policies = new PolicyProvider(options, new PolicyCache(options.AppId, options), logger);
        return new AtomUpdater(options, checker, new FakeDownloader(), new FakeInstaller(), policies,
            new FakeNotifier(), new FakeReporter(),
            new FileStateStore(options.AppId, Path.Combine(options.Cache.CacheDirectory!, "state")),
            new Platform.PlatformInfo(), logger);
    }

    private static Core.Models.UpdatePolicy PolicyWith(bool downloadOnPolling = false, int maxPostponeCount = 3) =>
        new()
        {
            Version = 4,
            PollingEnabled = true,
            PollingIntervalMinutes = 60,
            PollingJitterMinutes = 5,
            DownloadOnStartup = false,
            DownloadOnPolling = downloadOnPolling,
            DownloadOnManualCheck = false,
            DownloadOnForceUpdate = true,
            DownloadOnNetworkRecovery = false,
            DownloadOnResume = false,
            InstallMode = InstallMode.OnRestart,
            AllowForceUpdate = true,
            AllowDowngrade = false,
            AllowPostpone = true,
            MaxPostponeCount = maxPostponeCount,
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
