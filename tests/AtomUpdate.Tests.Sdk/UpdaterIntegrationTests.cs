using System.Text.Json;
using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Internal;
using AtomUpdate.Install;
using AtomUpdate.Logging;
using AtomUpdate.Policy;
using AtomUpdate.State;
using AtomUpdate.Tests.Sdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomUpdate.Tests.Sdk;

/// <summary>SDK↔更新器集成：内嵌更新器释放、启动配置字段、更新器状态回读补报。</summary>
public class UpdaterIntegrationTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    private readonly string _appId = "sdk-readback-" + Guid.NewGuid().ToString("N");

    private string StateFileDirectory => Path.Combine(PlatformPaths.ResolveStateRoot(), PlatformPaths.Sanitize(_appId));

    private string StateFilePath => Path.Combine(StateFileDirectory, "updater-state.json");

    // ---------- 内嵌更新器释放（UpdaterProvisioner） ----------

    [Theory]
    [InlineData(UpdatePlatform.Windows, UpdateArch.X64, "win-x64")]
    [InlineData(UpdatePlatform.Windows, UpdateArch.Arm64, "win-arm64")]
    [InlineData(UpdatePlatform.Macos, UpdateArch.X64, "osx-x64")]
    [InlineData(UpdatePlatform.Macos, UpdateArch.Arm64, "osx-arm64")]
    [InlineData(UpdatePlatform.Linux, UpdateArch.X64, "linux-x64")]
    [InlineData(UpdatePlatform.Linux, UpdateArch.Arm64, "linux-arm64")]
    [InlineData(UpdatePlatform.Linux, UpdateArch.X86, "linux-x86")]
    public void RuntimeIdentifier_MapsPlatformArch(UpdatePlatform platform, UpdateArch arch, string expected)
    {
        Assert.Equal(expected, UpdaterProvisioner.BuildRuntimeIdentifier(platform, arch));
    }

    [Fact]
    public void RuntimeIdentifier_UnknownPlatformOrArch_ReturnsNull()
    {
        Assert.Null(UpdaterProvisioner.BuildRuntimeIdentifier((UpdatePlatform)99, UpdateArch.X64));
        Assert.Null(UpdaterProvisioner.BuildRuntimeIdentifier(UpdatePlatform.Windows, (UpdateArch)99));
    }

    [Fact]
    public void LocateEmbedded_PrefersNuGetNativeAssets()
    {
        var source = Path.Combine(_tempRoot, "source");
        var primary = Path.Combine(source, "runtimes", "win-x64", "native");
        var flat = Path.Combine(source, "updater", "win-x64");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(flat);
        var fileName = UpdaterProvisioner.ExecutableWithExtension(OperatingSystem.IsWindows());
        File.WriteAllText(Path.Combine(primary, fileName), "native-asset");
        File.WriteAllText(Path.Combine(flat, fileName), "flat");

        Assert.Equal(Path.Combine(primary, fileName), UpdaterProvisioner.LocateEmbedded(source, "win-x64"));
        Assert.Null(UpdaterProvisioner.LocateEmbedded(Path.Combine(_tempRoot, "missing"), "win-x64"));
    }

    [Fact]
    public void EnsureProvisioned_CopiesToVersionedDirectoryAndMaintainsPointer()
    {
        var source = Path.Combine(_tempRoot, "source");
        var root = Path.Combine(_tempRoot, "atomupdate-root");
        var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
        var fileName = UpdaterProvisioner.ExecutableWithExtension(OperatingSystem.IsWindows());
        var embedded = Path.Combine(source, "runtimes", rid, "native", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(embedded)!);
        File.WriteAllText(embedded, "updater-binary-v1");
        var platform = new FakePlatformInfo(
            OperatingSystem.IsWindows() ? UpdatePlatform.Windows : OperatingSystem.IsMacOS() ? UpdatePlatform.Macos : UpdatePlatform.Linux,
            UpdateArch.X64, rid);
        var logger = new ExtensionsUpdateLogger(NullLoggerFactory.Instance);

        var provisioned = UpdaterProvisioner.EnsureProvisioned(platform, logger, source, root);

        var version = UpdaterProvisioner.VersionDirectoryName();
        var expected = Path.Combine(root, "updater", version, fileName);
        Assert.Equal(expected, provisioned);
        Assert.Equal("updater-binary-v1", File.ReadAllText(expected));
        Assert.Equal(version, File.ReadAllText(Path.Combine(root, "updater", UpdaterProvisioner.PointerFileName)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(expected).HasFlag(UnixFileMode.UserExecute));
        }

        // 幂等：重复释放内容不变；指针被外部改坏后再次释放会修正
        File.WriteAllText(Path.Combine(root, "updater", UpdaterProvisioner.PointerFileName), "0.0.1");
        Assert.Equal(expected, UpdaterProvisioner.EnsureProvisioned(platform, logger, source, root));
        Assert.Equal(version, File.ReadAllText(Path.Combine(root, "updater", UpdaterProvisioner.PointerFileName)));
        Assert.Equal("updater-binary-v1", File.ReadAllText(expected));
    }

    [Fact]
    public void EnsureProvisioned_MissingEmbeddedBinary_ReturnsNull()
    {
        var platform = new FakePlatformInfo();
        var logger = new ExtensionsUpdateLogger(NullLoggerFactory.Instance);

        Assert.Null(UpdaterProvisioner.EnsureProvisioned(platform, logger,
            Path.Combine(_tempRoot, "source-missing"), Path.Combine(_tempRoot, "root")));
    }

    [Fact]
    public void EnsureProvisioned_UnsupportedArch_ReturnsNull()
    {
        var logger = new ExtensionsUpdateLogger(NullLoggerFactory.Instance);
        Assert.Null(UpdaterProvisioner.EnsureProvisioned(new FakePlatformInfo((UpdatePlatform)99, UpdateArch.X64),
            logger, _tempRoot, Path.Combine(_tempRoot, "root")));
    }

    // ---------- 启动配置（04 §5.1） ----------

    [Fact]
    public void CreateLaunchConfig_MapsAllFields()
    {
        var options = Options();
        var platform = new Platform.PlatformInfo();
        var context = new InstallContext
        {
            PackagePath = Path.Combine(_tempRoot, "pkg.zip"),
            Update = new UpdateInfo
            {
                LatestVersion = "2.0.0",
                Package = new PackageAsset
                {
                    PackageType = PackageType.Zip,
                    Type = AssetKind.Delta,
                    Arch = UpdateArch.X64,
                    Url = "https://cdn.test/delta.zip",
                    Sha256 = new string('a', 64),
                    SilentArgs = "/S",
                    InstallArgs = "/DIR=custom"
                }
            },
            Mode = InstallMode.OnRestart,
            RestartPath = Path.Combine(_tempRoot, "MyApp.exe"),
            RestartArgs = "--skip-update-check"
        };

        var config = UpdateInstaller.CreateLaunchConfig(context, options, platform, "inst-1", "fp-1");

        Assert.Equal(context.PackagePath, config.PackagePath);
        Assert.Equal(_tempRoot, config.TargetPath);
        Assert.Equal("1.0.0", config.FromVersion);
        Assert.Equal("2.0.0", config.ToVersion);
        Assert.Equal("zip", config.PackageType);
        Assert.Equal("onrestart", config.InstallMode);
        Assert.Equal("delta", config.Kind);
        Assert.Equal(new string('a', 64), config.Sha256);
        Assert.Equal(context.RestartPath, config.RestartPath);
        Assert.Equal("--skip-update-check", config.RestartArgs);
        Assert.Equal(options.ServerUrl, config.ServerUrl);
        Assert.Equal("inst-1", config.InstallationId);
        Assert.Equal("fp-1", config.MachineFingerprint);
        Assert.Equal(options.AppId, config.AppId);
        Assert.Equal(options.ProjectKey, config.ProjectKey);
        Assert.Equal(platform.Platform.ToString().ToLowerInvariant(), config.Platform);
        // 包架构优先：包带 Arch=X64 时启动配置用包架构
        Assert.Equal("x64", config.Arch);
        Assert.Equal("/S", config.SilentArgs);
        Assert.Equal("/DIR=custom", config.InstallArgs);
        Assert.Equal(5, config.WaitForMainProcessExitSeconds);
    }

    [Fact]
    public void CreateLaunchConfig_FullPackage_KindLeftNullForAutoDetection()
    {
        var context = new InstallContext
        {
            PackagePath = Path.Combine(_tempRoot, "pkg.zip"),
            Update = new UpdateInfo
            {
                LatestVersion = "2.0.0",
                FullPackage = new PackageAsset
                {
                    PackageType = PackageType.Zip,
                    Type = AssetKind.Full,
                    Arch = UpdateArch.X64,
                    Url = "https://cdn.test/full.zip",
                    Sha256 = new string('b', 64)
                }
            },
            Mode = InstallMode.Manual,
            RestartPath = Path.Combine(_tempRoot, "MyApp.exe")
        };

        var config = UpdateInstaller.CreateLaunchConfig(context, Options(), new Platform.PlatformInfo(), null, null);

        Assert.Null(config.Kind);
        Assert.Equal("manual", config.InstallMode);
    }

    [Fact]
    public void CreateLaunchConfig_Arch_PackageArchPreferred_DeviceArchFallback()
    {
        // arm64 设备回落 x64 包：启动配置须带包架构，否则更新器按设备架构验签必失败
        var arm64Platform = new FakePlatformInfo(UpdatePlatform.Windows, UpdateArch.Arm64);
        var withPackage = new InstallContext
        {
            PackagePath = Path.Combine(_tempRoot, "pkg.zip"),
            Update = new UpdateInfo
            {
                LatestVersion = "2.0.0",
                FullPackage = new PackageAsset
                {
                    PackageType = PackageType.Zip,
                    Arch = UpdateArch.X64,
                    Url = "https://cdn.test/full.zip",
                    Sha256 = new string('b', 64)
                }
            },
            Mode = InstallMode.Manual,
            RestartPath = Path.Combine(_tempRoot, "MyApp.exe")
        };

        var config = UpdateInstaller.CreateLaunchConfig(withPackage, Options(), arm64Platform, null, null);
        Assert.Equal("x64", config.Arch);

        // 包缺省（如无更新资产下发）回落设备架构
        var withoutPackage = new InstallContext
        {
            PackagePath = Path.Combine(_tempRoot, "pkg.zip"),
            Update = new UpdateInfo { LatestVersion = "2.0.0" },
            Mode = InstallMode.Manual,
            RestartPath = Path.Combine(_tempRoot, "MyApp.exe")
        };

        var fallback = UpdateInstaller.CreateLaunchConfig(withoutPackage, Options(), arm64Platform, null, null);
        Assert.Equal("arm64", fallback.Arch);
    }

    // ---------- 更新器状态回读补报（04 §4.4/§5.3） ----------

    [Fact]
    public async Task StartupReport_UpdaterState_Completed_ReportsAndMarksReported()
    {
        WriteStateFile(new
        {
            projectKey = "test",
            appId = _appId,
            installationId = "inst-42",
            fromVersion = "1.0.0",
            toVersion = "2.0.0",
            stage = "completed",
            result = "success",
            installMode = "onrestart",
            logPath = "/logs/updater.log",
            timestamp = DateTimeOffset.UtcNow,
            reported = false
        });
        var scope = BuildScope();

        var ok = await scope.Updater.ReportStartupAsync();

        Assert.True(ok);
        var install = Assert.IsType<InstallReport>(scope.Reporter.Reports.OfType<InstallReport>().Single());
        Assert.Equal(UpdateStage.Completed, install.Stage);
        Assert.Equal(InstallMode.OnRestart, install.InstallMode);
        Assert.Equal("inst-42", install.InstallationId);
        Assert.Equal("2.0.0", install.ToVersion);
        Assert.Equal("/logs/updater.log", install.LogPath);
        Assert.True(ReadReported());
        Assert.Single(scope.Reporter.Reports.OfType<StartupReport>()); // 状态补报后仍发启动回执
    }

    [Fact]
    public async Task StartupReport_UpdaterState_RolledBack_MapsErrorFields()
    {
        WriteStateFile(new
        {
            projectKey = "test",
            appId = _appId,
            installationId = "inst-42",
            fromVersion = "1.0.0",
            toVersion = "2.0.0",
            stage = "rolledBack",
            result = "rolledBack",
            installMode = "manual",
            elevated = true,
            elevationRejected = false,
            errorCode = "0x1009",
            errorMessage = "回滚失败",
            reported = false
        });
        var scope = BuildScope();

        await scope.Updater.ReportStartupAsync();

        var install = Assert.IsType<InstallReport>(scope.Reporter.Reports.OfType<InstallReport>().Single());
        Assert.Equal(UpdateStage.RolledBack, install.Stage);
        Assert.Equal(InstallMode.Manual, install.InstallMode);
        Assert.True(install.Elevated);
        Assert.Equal("0x1009", install.ErrorCode);
        Assert.Equal("回滚失败", install.ErrorMessage);
        Assert.True(ReadReported());
    }

    [Fact]
    public async Task StartupReport_UpdaterState_AlreadyReported_NoDuplicate()
    {
        WriteStateFile(new
        {
            appId = _appId,
            toVersion = "2.0.0",
            stage = "completed",
            reported = true
        });
        var scope = BuildScope();

        await scope.Updater.ReportStartupAsync();

        Assert.Empty(scope.Reporter.Reports.OfType<InstallReport>());
        Assert.Single(scope.Reporter.Reports.OfType<StartupReport>());
    }

    [Fact]
    public async Task StartupReport_UpdaterState_QueueingOff_KeepsReportedFalseForRetry()
    {
        WriteStateFile(new
        {
            appId = _appId,
            installationId = "inst-42",
            toVersion = "2.0.0",
            stage = "failed",
            reported = false
        });
        var options = Options();
        options.Report.FallbackToLocal = false;
        options.Cache.EnableOfflineReport = false;
        var scope = BuildScope(options);
        scope.Reporter.Succeed = false; // 未送达且无离线队列 → 保留 reported=false 等下次启动

        await scope.Updater.ReportStartupAsync();

        Assert.Single(scope.Reporter.Reports.OfType<InstallReport>());
        Assert.False(ReadReported());
    }

    [Fact]
    public async Task StartupReport_UpdaterState_NotDelivered_PendingQueueOwnsDelivery()
    {
        WriteStateFile(new
        {
            appId = _appId,
            installationId = "inst-42",
            toVersion = "2.0.0",
            stage = "failed",
            reported = false
        });
        var scope = BuildScope();
        scope.Reporter.Succeed = false; // 未送达但默认具备离线队列 → 交付责任转移，防重复投递

        await scope.Updater.ReportStartupAsync();

        Assert.Single(scope.Reporter.Reports.OfType<InstallReport>());
        Assert.True(ReadReported());
    }

    [Fact]
    public async Task StartupReport_UpdaterState_CorruptFile_IgnoredWithoutThrow()
    {
        Directory.CreateDirectory(StateFileDirectory);
        File.WriteAllText(StateFilePath, "{ not-json");
        var scope = BuildScope();

        await scope.Updater.ReportStartupAsync();

        Assert.Empty(scope.Reporter.Reports.OfType<InstallReport>());
        Assert.Equal("{ not-json", File.ReadAllText(StateFilePath));
    }

    [Fact]
    public async Task StartupReport_UpdaterState_ReportDisabled_SkipsReadback()
    {
        WriteStateFile(new
        {
            appId = _appId,
            installationId = "inst-42",
            toVersion = "2.0.0",
            stage = "completed",
            reported = false
        });
        var options = Options();
        options.Report.Enabled = false;
        var scope = BuildScope(options);

        await scope.Updater.ReportStartupAsync();

        Assert.Empty(scope.Reporter.Reports.OfType<InstallReport>());
        Assert.False(ReadReported()); // 状态文件保持原样，不消费
    }

    private AtomUpdateOptions Options() => new()
    {
        ServerUrl = "http://localhost:5062",
        ProjectKey = "test",
        AppId = _appId,
        CurrentVersion = "1.0.0",
        Cache = new CacheOptions { CacheDirectory = _tempRoot }
    };

    private sealed record Scope(AtomUpdater Updater, FakeReporter Reporter);

    private Scope BuildScope(AtomUpdateOptions? options = null)
    {
        options ??= Options();
        var logger = new ExtensionsUpdateLogger(NullLoggerFactory.Instance);
        var policies = new PolicyProvider(options, new PolicyCache(options.AppId, options), logger);
        var reporter = new FakeReporter();
        var updater = new AtomUpdater(options,
            new FakeChecker(OperationResult<CheckPayload>.Ok(new CheckPayload { HasUpdate = false })),
            new FakeDownloader(), new FakeInstaller(), policies, new FakeNotifier(), reporter,
            new FileStateStore(options.AppId, Path.Combine(options.Cache.CacheDirectory!, "state")),
            new Platform.PlatformInfo(), logger);
        return new Scope(updater, reporter);
    }

    private void WriteStateFile(object state)
    {
        Directory.CreateDirectory(StateFileDirectory);
        File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state, SdkTestJson.Options));
    }

    private bool ReadReported()
    {
        var json = JsonDocument.Parse(File.ReadAllText(StateFilePath)).RootElement;
        return json.GetProperty("reported").GetBoolean();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }

        if (Directory.Exists(StateFileDirectory))
        {
            Directory.Delete(StateFileDirectory, recursive: true);
        }
    }
}
