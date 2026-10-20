using System.Diagnostics;
using System.Text.Json;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.State;

namespace AtomUpdate.Install;

/// <summary>更新器启动配置（04 §5.1 临时配置文件，restrictive ACL/清理由更新器细化）。
/// 字段名与更新器 UpdaterTaskFile 顶层运行字段一一对应（camelCase）。</summary>
public sealed class UpdaterLaunchConfig
{
    public string PackagePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string? FromVersion { get; set; }
    public string ToVersion { get; set; } = "";
    public string PackageType { get; set; } = "zip";
    public string InstallMode { get; set; } = "onRestart";

    /// <summary>full/delta（delta 显式声明；full 缺省由更新器按包内容自动识别）。</summary>
    public string? Kind { get; set; }

    public string? Sha256 { get; set; }
    public string? Signature { get; set; }
    public string? KeyId { get; set; }
    public string? RestartPath { get; set; }
    public string? RestartArgs { get; set; }
    public string? ServerUrl { get; set; }
    public string? InstallationId { get; set; }
    public string? MachineFingerprint { get; set; }

    /// <summary>应用标识（状态目录隔离/互斥命名/回传内容）。</summary>
    public string? AppId { get; set; }

    public string? ProjectKey { get; set; }

    /// <summary>windows/macos/linux（签名内容组成）。</summary>
    public string? Platform { get; set; }

    /// <summary>x64/arm64/x86（签名内容组成）。</summary>
    public string? Arch { get; set; }

    /// <summary>exe/msi 静默参数（包资产元数据）。</summary>
    public string? SilentArgs { get; set; }

    /// <summary>exe/msi 附加安装参数（包资产元数据）。</summary>
    public string? InstallArgs { get; set; }

    /// <summary>等待主程序退出秒数（04 §5.2，超时强制继续）。</summary>
    public int? WaitForMainProcessExitSeconds { get; set; }
}

/// <summary>安装调度（04 §3.3/§5.1/§5.2）：写临时配置 → 启动更新器；onRestart 模式落调度状态由宿主退出触发。</summary>
public sealed class UpdateInstaller(
    AtomUpdateOptions options,
    IStateStore stateStore,
    IPlatformInfo platformInfo,
    IUpdateLogger logger) : IUpdateInstaller
{
    public async Task<InstallResult> InstallAsync(InstallContext context, CancellationToken ct = default)
    {
        var updaterPath = ResolveUpdaterPath();
        if (updaterPath is null)
        {
            if (context.Mode == InstallMode.OnRestart)
            {
                // 更新器未随包分发（如源码引用开发场景）时允许纯调度：落调度状态
                await stateStore.SetStringAsync(StateKeys.ScheduledInstall, context.PackagePath, ct);
                logger.Warning($"已调度 onRestart 安装，但更新器不可用（配置 Updater.UpdaterPath 或使用 NuGet 分发包）：{context.PackagePath}");
                return InstallResult.ScheduledOk();
            }

            return InstallResult.Fail(ErrorCodes.UpdaterNotFound,
                "更新器不可用：未配置 Updater.UpdaterPath 且包内未包含内嵌更新器");
        }

        var config = await WriteLaunchConfigAsync(context, ct);

        try
        {
            var arguments = $"--config \"{config}\"";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            logger.Information($"更新器已启动：{updaterPath}（PID {process?.Id}）");
        }
        catch (Exception exception)
        {
            return InstallResult.Fail(ErrorCodes.InstallFailed, "更新器启动失败", exception);
        }

        return InstallResult.ScheduledOk();
    }

    /// <summary>更新器可执行文件：优先 UpdaterPath；AutoExtractUpdater 释放内嵌内容到版本化目录（04 §5.2）。</summary>
    private string? ResolveUpdaterPath()
    {
        if (!string.IsNullOrEmpty(options.Updater.UpdaterPath))
        {
            if (File.Exists(options.Updater.UpdaterPath))
            {
                return options.Updater.UpdaterPath;
            }

            logger.Warning($"配置的更新器路径不存在：{options.Updater.UpdaterPath}");
            return null;
        }

        if (!options.Updater.AutoExtractUpdater)
        {
            return null;
        }

        return UpdaterProvisioner.EnsureProvisioned(platformInfo, logger);
    }

    private async Task<string> WriteLaunchConfigAsync(InstallContext context, CancellationToken ct)
    {
        var config = CreateLaunchConfig(context, options, platformInfo,
            await stateStore.GetStringAsync(InstallationIdentity.InstallationIdKey, ct),
            await stateStore.GetStringAsync(InstallationIdentity.FingerprintKey, ct));

        var directory = Path.Combine(Path.GetTempPath(), "atomupdate");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"updater-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(config, UpdateReporterJson.Options), ct);
        return path;
    }

    /// <summary>构造启动配置（04 §5.1）：字段与更新器 UpdaterTaskFile 顶层运行字段一一对应（ConsistencyTests 反射校验）。</summary>
    public static UpdaterLaunchConfig CreateLaunchConfig(InstallContext context, AtomUpdateOptions options,
        IPlatformInfo platformInfo, string? installationId, string? machineFingerprint)
    {
        var package = context.Update.ResolvePackage();
        return new UpdaterLaunchConfig
        {
            PackagePath = context.PackagePath,
            TargetPath = context.RestartPath is null ? "" : Path.GetDirectoryName(context.RestartPath) ?? "",
            FromVersion = options.CurrentVersion,
            ToVersion = context.Update.LatestVersion,
            PackageType = package?.PackageType.ToString().ToLowerInvariant() ?? "zip",
            InstallMode = context.Mode.ToString().ToLowerInvariant(),
            // 增量包显式声明 kind=delta；全量缺省留空，由更新器按包内容自动识别
            Kind = package?.Type == AssetKind.Delta ? "delta" : null,
            Sha256 = package?.Sha256,
            Signature = package?.Signature,
            KeyId = package?.KeyId,
            RestartPath = context.RestartPath,
            RestartArgs = context.RestartArgs,
            ServerUrl = options.ServerUrl,
            InstallationId = installationId,
            MachineFingerprint = machineFingerprint,
            AppId = options.AppId,
            ProjectKey = options.ProjectKey,
            Platform = platformInfo.Platform.ToString().ToLowerInvariant(),
            // 架构以包资产为准：arm64 设备回落 x64 包时，验签内容须用包架构；包未带架构回落设备架构
            Arch = package?.Arch is { } arch
                ? arch.ToString().ToLowerInvariant()
                : platformInfo.Arch.ToString().ToLowerInvariant(),
            SilentArgs = package?.SilentArgs,
            InstallArgs = package?.InstallArgs,
            WaitForMainProcessExitSeconds = options.Updater.WaitForMainProcessExit
        };
    }
}

/// <summary>状态键常量。</summary>
public static class StateKeys
{
    public const string ScheduledInstall = "install.scheduled";
    public const string PostponeCount = "postpone.count";
    public const string PostponeUntil = "postpone.until";
}

/// <summary>SDK 内部 JSON 序列化（与 Core 契约同风格）。</summary>
public static class UpdateReporterJson
{
    public static JsonSerializerOptions Options { get; } = Core.Serialization.AtomUpdateJson.Create();
}
