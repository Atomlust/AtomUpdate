using System.ComponentModel;
using System.Diagnostics;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Replace;

namespace AtomUpdate.Updater.Installers;

/// <summary>exe 安装器（04 §4.7，MVP Inno Setup）：静默安装 → 等待退出 → 非 0 退出码视为失败（0x1007）。
/// 安装器自行管理文件替换，无回滚清单（返回 null）。</summary>
public sealed class ExeInstaller(UpdaterTaskFile task, UpdaterLog log) : IPackageInstaller
{
    public Task<BackupManifest?> InstallAsync(string targetRoot, CancellationToken ct)
    {
        var arguments = BuildArguments();
        log.Information($"运行安装器：{task.PackagePath} {arguments}".TrimEnd());

        var startInfo = new ProcessStartInfo
        {
            FileName = task.PackagePath!,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = targetRoot
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new UpdaterFailureException(ExitCodes.InstallerFailed, $"安装器进程未能启动：{task.PackagePath}");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new UpdaterFailureException(ExitCodes.InstallerFailed,
                    $"安装器退出码 {process.ExitCode}（{task.PackagePath}）");
            }

            log.Information("安装器执行完成（退出码 0）");
            return Task.FromResult<BackupManifest?>(null);
        }
        catch (Win32Exception exception)
        {
            throw new UpdaterFailureException(ExitCodes.InstallerFailed, $"安装器无法执行：{exception.Message}", exception);
        }
    }

    /// <summary>参数来源：包元数据 silentArgs / installArgs 优先，其次安装节 installArgs（--install-args 已合并进该字段）。</summary>
    private string BuildArguments()
    {
        var parts = new[] { task.SilentArgs, task.InstallArgs ?? task.Install.InstallArgs };
        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
