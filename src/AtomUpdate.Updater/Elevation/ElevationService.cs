using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Elevation;

/// <summary>提权（04 §4.10）：预判（写目标目录探测）→ 自我提权（Windows runas / macOS osascript / Linux pkexec·sudo）→ 防重复 UAC（已提权不再提）；
/// 被拒 → 0x1003（状态文件 elevationRejected=true，回传后由 SDK 授权界面重试）。</summary>
public static class ElevationService
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    public static bool IsElevated()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }

            return GetEuid() == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>提权预判：写目标目录探测 + 配置模式（onDemand/always/never、skipIfNotNeeded）。</summary>
    public static bool NeedsElevation(UpdaterTaskFile task, string targetRoot, UpdaterLog log)
    {
        var mode = task.Elevation.Mode.ToLowerInvariant();
        if (mode == ElevationModes.Never)
        {
            return false;
        }

        if (mode == ElevationModes.Always)
        {
            return !IsElevated();
        }

        // onDemand：探测目标目录可写性
        if (!task.Elevation.SkipIfNotNeeded)
        {
            return !IsElevated();
        }

        if (IsElevated())
        {
            return false;
        }

        var probe = Path.Combine(targetRoot, $".atomupdate-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            log.Information("目标目录不可写，需要提权");
            return true;
        }
        catch (Exception exception)
        {
            log.Debug($"提权探测异常（按不需提权继续）：{exception.Message}");
            return false;
        }
    }

    /// <summary>自我提权并等待其完成，镜像子进程退出码。被拒/取消 → 0x1003。</summary>
    public static int ElevateAndWait(string[] originalArgs, UpdaterTaskFile task, UpdaterLog log)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            throw new UpdaterFailureException(ExitCodes.Unknown, "无法解析更新器自身可执行文件路径");
        }

        var arguments = BuildElevatedArguments(originalArgs);
        var attempts = ParseRetry(task.Elevation.Retry);
        for (var attempt = 1; ; attempt++)
        {
            var exitCode = RunElevatedOnce(executable, arguments, log);
            if (exitCode != ExitCodes.ElevationRejected || attempt >= attempts)
            {
                return exitCode;
            }

            log.Warning($"提权被拒，重试 {attempt}/{attempts}（elevation.retry）");
        }
    }

    /// <summary>retry: unlimited 或数字。跨次授权循环由 SDK 负责，本进程默认单次尝试（unlimited 亦按 1 处理）。</summary>
    private static int ParseRetry(string value) =>
        int.TryParse(value, out var number) && number > 1 ? number : 1;

    private static string[] BuildElevatedArguments(string[] originalArgs)
    {
        if (originalArgs.Contains("--elevated", StringComparer.OrdinalIgnoreCase))
        {
            return originalArgs;
        }

        var result = new string[originalArgs.Length + 1];
        originalArgs.CopyTo(result, 0);
        result[^1] = "--elevated";
        return result;
    }

    private static int RunElevatedOnce(string executable, string[] args, UpdaterLog log)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunWindows(executable, args, log);
        }

        if (OperatingSystem.IsMacOS())
        {
            return RunMac(executable, args, log);
        }

        return RunLinux(executable, args, log);
    }

    private static int RunWindows(string executable, string[] args, UpdaterLog log)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable, JoinArgs(args))
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new UpdaterFailureException(ExitCodes.ElevationRejected, "提权进程未能启动");
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            // 1223 = ERROR_CANCELLED（用户在 UAC 对话框选择"否"）
            throw new UpdaterFailureException(ExitCodes.ElevationRejected, "用户拒绝提权（UAC 已取消）", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new UpdaterFailureException(ExitCodes.ElevationRejected, "提权启动失败", exception);
        }
    }

    private static int RunMac(string executable, string[] args, UpdaterLog log)
    {
        // osascript 不传递子进程退出码：包一层把 $? 写入临时文件后回读
        var exitFile = Path.Combine(Path.GetTempPath(), "atomupdate", $"elevated-exit-{Environment.ProcessId}");
        Directory.CreateDirectory(Path.GetDirectoryName(exitFile)!);
        try
        {
            if (File.Exists(exitFile))
            {
                File.Delete(exitFile);
            }
        }
        catch (Exception)
        {
            // 忽略残留
        }

        var shellCommand = string.Join(' ', new[] { Quote(executable) }.Concat(args.Select(Quote))) +
                           $" ; echo -n $? > {Quote(exitFile)}";
        var script = $"do shell script \"{EscapeForAppleScript(shellCommand)}\" with administrator privileges";

        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new UpdaterFailureException(ExitCodes.ElevationRejected, "osascript 未能启动");
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (File.Exists(exitFile))
            {
                var text = File.ReadAllText(exitFile).Trim();
                if (int.TryParse(text, out var childExit))
                {
                    return childExit;
                }
            }

            if (process.ExitCode != 0)
            {
                log.Error($"提权执行失败：{stderr.Trim()}");
                throw new UpdaterFailureException(ExitCodes.ElevationRejected, "用户拒绝提权或授权失败");
            }

            return ExitCodes.Success;
        }
        catch (Win32Exception exception)
        {
            throw new UpdaterFailureException(ExitCodes.ElevationRejected, "osascript 启动失败", exception);
        }
    }

    private static int RunLinux(string executable, string[] args, UpdaterLog log)
    {
        if (TryRunElevatedTool("pkexec", executable, args, out var exitCode))
        {
            if (exitCode is 126 or 127)
            {
                throw new UpdaterFailureException(ExitCodes.ElevationRejected,
                    $"提权被拒（pkexec 退出码 {exitCode}：授权取消或不可用）");
            }

            return exitCode;
        }

        // pkexec 不存在时降级 sudo -n（无 tty 场景非交互）
        if (TryRunElevatedTool("sudo", executable, ["-n", .. args], out exitCode))
        {
            if (exitCode != 0)
            {
                throw new UpdaterFailureException(ExitCodes.ElevationRejected, $"提权被拒（sudo 退出码 {exitCode}）");
            }

            return exitCode;
        }

        throw new UpdaterFailureException(ExitCodes.ElevationRejected,
            "未找到可用的提权程序（pkexec/sudo），无法获取管理员权限");
    }

    private static bool TryRunElevatedTool(string tool, string executable, string[] args, out int exitCode)
    {
        exitCode = 0;
        var startInfo = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(executable);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            exitCode = process.ExitCode;
            return true;
        }
        catch (Win32Exception)
        {
            return false; // 工具不存在
        }
    }

    private static string JoinArgs(IEnumerable<string> args) =>
        string.Join(' ', args.Select(Quote));

    private static string Quote(string value) =>
        value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')
            ? value
            : $"\"{value.Replace("\"", "\\\"")}\"";

    private static string EscapeForAppleScript(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
