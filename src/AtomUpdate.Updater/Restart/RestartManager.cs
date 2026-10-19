using System.Diagnostics;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Restart;

/// <summary>重启主程序（04 §5.2）：等待主程序退出（超时强制继续）→ 安装完成后 delayMs 再以 --skip-update-check 启动（防循环）。
/// 重启失败 → 0x100A（安装结果不回退）。</summary>
public static class RestartManager
{
    public static void WaitForMainProcessExit(UpdaterTaskFile task, int waitMs, UpdaterLog log)
    {
        var restartPath = task.RestartPath;
        if (string.IsNullOrWhiteSpace(restartPath))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(restartPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, waitMs));
        while (true)
        {
            var alive = AnyAlive(name);
            if (!alive)
            {
                log.Debug($"主程序（{name}）已退出，继续安装");
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                log.Warning($"等待主程序退出超时（{waitMs}ms），强制继续");
                return;
            }

            Thread.Sleep(200);
        }
    }

    private static bool AnyAlive(string name)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(name);
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            return processes.Any(process =>
            {
                try
                {
                    return !process.HasExited;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static void Restart(UpdaterTaskFile task, UpdaterLog log)
    {
        if (string.IsNullOrWhiteSpace(task.RestartPath))
        {
            log.Information("未配置重启目标（--restart），跳过重启");
            return;
        }

        var delayMs = task.Restart.DelayMs;
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        var args = !string.IsNullOrWhiteSpace(task.RestartArgs) ? task.RestartArgs : task.Restart.Args;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = task.RestartPath,
                Arguments = args ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(task.RestartPath) ?? ""
            });
            log.Information($"已重启主程序：{task.RestartPath}（PID {process?.Id}）args={args}");
        }
        catch (Exception exception)
        {
            throw new UpdaterFailureException(ExitCodes.RestartFailed, $"重启主程序失败：{exception.Message}", exception);
        }
    }
}
