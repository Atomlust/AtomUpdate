using AtomUpdate.Updater.Replace;

namespace AtomUpdate.Updater.Installers;

/// <summary>包安装器抽象（04 §4.7，接口一次到位）：MVP 实现 zip（替换引擎）与 exe（静默安装）。
/// 安装器失败统一抛 UpdaterFailureException(0x1007)；zip 链路经 FileReplacer 携带回滚清单。</summary>
public interface IPackageInstaller
{
    /// <summary>执行安装；返回回滚清单（zip 替换链路），无回滚能力（exe 等）返回 null。</summary>
    Task<BackupManifest?> InstallAsync(string targetRoot, CancellationToken ct);
}
