using System.Reflection;
using System.Text.Json;
using AtomUpdate.Install;
using AtomUpdate.Updater;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Json;

namespace AtomUpdate.Tests.Updater;

/// <summary>跨项目不变量：更新器本地定义必须与 Core/SDK 契约一致（更新器不引用 Core/SDK，值靠测试守护）。</summary>
public class ConsistencyTests
{
    [Fact]
    public void UpdaterExitCodes_MatchCoreConstants()
    {
        var updaterType = typeof(AtomUpdate.Updater.ExitCodes);
        var coreType = typeof(AtomUpdate.Core.Constants.ExitCodes);
        var updaterFields = updaterType.GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.NotEmpty(updaterFields);
        foreach (var field in updaterFields)
        {
            var coreField = coreType.GetField(field.Name, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(coreField);
            Assert.Equal(coreField!.GetRawConstantValue(), field.GetRawConstantValue());
        }
    }

    [Fact]
    public void UpdaterPaths_SanitizeEmpty_UsesDefaultSegment()
    {
        Assert.Equal("default", UpdaterPaths.Sanitize(""));
        Assert.Equal("app1", UpdaterPaths.Sanitize("app1"));
    }

    /// <summary>SDK 启动配置每个字段都必须能被更新器任务文件接收（新增字段不会静默丢弃，04 §5.1）。</summary>
    [Fact]
    public void SdkLaunchConfig_Properties_ExistOnUpdaterTaskFile()
    {
        var taskProperties = typeof(UpdaterTaskFile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        foreach (var property in typeof(UpdaterLaunchConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Contains(property.Name, taskProperties);
        }
    }

    /// <summary>SDK 序列化的临时配置必须能被更新器 source-gen 反序列化并逐字段还原（camelCase 契约）。</summary>
    [Fact]
    public void SdkLaunchConfig_RoundTripsIntoUpdaterTaskFile()
    {
        var config = new UpdaterLaunchConfig
        {
            PackagePath = "/tmp/pkg/app-2.0.0.zip",
            TargetPath = "/opt/app",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            PackageType = "zip",
            InstallMode = "onrestart",
            Kind = "delta",
            Sha256 = new string('a', 64),
            Signature = "MEQCIG...",
            KeyId = "key-2026",
            RestartPath = "/opt/app/MyApp",
            RestartArgs = "--skip-update-check",
            ServerUrl = "https://update.test",
            InstallationId = "inst-1",
            MachineFingerprint = "fp-1",
            AppId = "com.test.app",
            ProjectKey = "test",
            Platform = "linux",
            Arch = "x64",
            SilentArgs = "/S",
            InstallArgs = "/DIR=/opt/app",
            WaitForMainProcessExitSeconds = 7
        };

        var json = JsonSerializer.Serialize(config, AtomUpdate.Core.Serialization.AtomUpdateJson.Create());
        var task = JsonSerializer.Deserialize(json, UpdaterJsonContext.Default.UpdaterTaskFile);

        Assert.NotNull(task);
        foreach (var property in typeof(UpdaterLaunchConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Equal(property.GetValue(config),
                typeof(UpdaterTaskFile).GetProperty(property.Name)!.GetValue(task));
        }
    }

    /// <summary>SDK 状态补报读取的更新器状态字段必须与更新器写出的状态文件同构（04 §4.4）。</summary>
    [Fact]
    public void SdkUpdaterStateFile_MirrorsUpdaterState()
    {
        var sdkProperties = typeof(AtomUpdate.State.UpdaterStateFile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        foreach (var property in typeof(AtomUpdate.Updater.State.UpdaterState)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Contains(property.Name, sdkProperties);
        }
    }
}
