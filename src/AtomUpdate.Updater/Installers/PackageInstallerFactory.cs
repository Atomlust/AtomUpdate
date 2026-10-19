using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Installers;

/// <summary>安装器工厂（04 §4.7）：zip → 替换引擎；exe → 静默安装；其余（msi/dmg/pkg/appimage/deb/rpm）→ 明确失败。</summary>
public static class PackageInstallerFactory
{
    public static IPackageInstaller Create(UpdaterTaskFile task, UpdaterLog log) =>
        task.PackageType?.ToLowerInvariant() switch
        {
            PackageTypes.Zip => new ZipInstaller(task, log),
            PackageTypes.Exe => new ExeInstaller(task, log),
            _ => new UnsupportedInstaller(task, log)
        };
}
