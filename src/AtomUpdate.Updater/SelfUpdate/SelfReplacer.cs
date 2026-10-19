using System.Diagnostics;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Verification;

namespace AtomUpdate.Updater.SelfUpdate;

/// <summary>双进程自升级的替换端（04 §4.12）：New.exe --replace-old 等待旧进程退出 → 备份 .bak → 覆盖 → 校验 → 删备份。
/// 替换失败恢复 .bak；恢复失败记录日志继续用旧版本（原任务继续由本进程执行）。</summary>
public static class SelfReplacer
{
    public static bool Replace(string oldPath, int? oldPid, UpdaterLog log)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self))
        {
            log.Error("无法解析更新器自身路径，跳过自替换");
            return false;
        }

        if (PathsEqual(self, oldPath))
        {
            log.Debug("--replace-old 指向当前进程自身，跳过替换");
            return true;
        }

        if (oldPid is { } pid)
        {
            WaitProcessExit(pid, TimeSpan.FromSeconds(30), log);
        }

        var backup = oldPath + ".bak";
        try
        {
            var oldExists = File.Exists(oldPath);
            if (oldExists)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(oldPath))!);
                File.Copy(oldPath, backup, overwrite: true);
            }

            var staging = oldPath + ".new";
            File.Copy(self, staging, overwrite: true);
            try
            {
                File.Move(staging, oldPath, overwrite: true);
            }
            catch
            {
                TryDelete(staging);
                throw;
            }

            // 校验：长度 + 哈希一致
            if (new FileInfo(oldPath).Length != new FileInfo(self).Length ||
                !string.Equals(PackageVerifier.ComputeSha256(oldPath), PackageVerifier.ComputeSha256(self),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("替换后文件校验不一致");
            }

            TryDelete(backup);
            log.Information($"更新器自替换完成：{oldPath}");
            return true;
        }
        catch (Exception exception)
        {
            log.Error($"更新器自替换失败：{exception.Message}", exception);

            try
            {
                if (File.Exists(backup))
                {
                    File.Copy(backup, oldPath, overwrite: true);
                    log.Information("已从 .bak 恢复旧更新器");
                }
            }
            catch (Exception restoreException)
            {
                log.Error("恢复 .bak 失败，继续使用旧版本（原文件可能未受影响）", restoreException);
            }

            return false;
        }
    }

    private static void WaitProcessExit(int pid, TimeSpan timeout, UpdaterLog log)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                log.Debug($"旧更新器进程（PID {pid}）已退出");
                return;
            }

            log.Warning($"等待旧更新器进程退出超时（{timeout.TotalSeconds:F0}s），强制继续");
        }
        catch (ArgumentException)
        {
            // 进程已不存在
        }
        catch (Exception exception)
        {
            log.Warning($"等待旧进程退出异常（继续）：{exception.Message}");
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响主流程
        }
    }
}
