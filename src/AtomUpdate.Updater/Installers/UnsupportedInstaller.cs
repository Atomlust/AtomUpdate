using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Replace;

namespace AtomUpdate.Updater.Installers;

/// <summary>未支持的包类型（04 §4.7）：msi 二阶段；dmg/pkg/AppImage/deb/rpm 第三阶段。明确失败（0x1007）而非静默跳过。</summary>
public sealed class UnsupportedInstaller(UpdaterTaskFile task, UpdaterLog log) : IPackageInstaller
{
    public Task<BackupManifest?> InstallAsync(string targetRoot, CancellationToken ct)
    {
        log.Error($"包类型 {task.PackageType} 暂未支持");
        throw new UpdaterFailureException(ExitCodes.InstallerFailed,
            $"包类型 {task.PackageType} 暂未支持（MVP 支持 zip 与 exe；msi 二阶段，dmg/pkg/AppImage/deb/rpm 第三阶段）");
    }
}
