using System.Diagnostics;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Hooks;

/// <summary>钩子系统（04 §4.8）：Windows .bat/.cmd→cmd /c、.ps1→powershell -ExecutionPolicy Bypass、.exe 直接；Unix .sh→/bin/sh。
/// 超时 hookTimeoutSeconds（视为失败）；hookFailPolicy abort（中断+回滚）/ continue；环境变量注入 ATOMUPDATE_*；工作目录 = 目标目录。</summary>
public sealed class HookRunner(UpdaterTaskFile task, UpdaterLog log)
{
    /// <summary>执行某阶段钩子；返回 false 表示按 abort 策略应中断。</summary>
    public async Task<bool> RunAsync(string stage, string targetRoot, CancellationToken ct = default)
    {
        HookCommandList? commands = null;
        foreach (var pair in task.Install.Hooks)
        {
            if (string.Equals(pair.Key, stage, StringComparison.OrdinalIgnoreCase))
            {
                commands = pair.Value;
                break;
            }
        }

        if (commands is null || commands.Count == 0)
        {
            return true;
        }

        var abortPolicy = string.Equals(task.Install.HookFailPolicy, HookFailPolicies.Abort,
            StringComparison.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            log.Information($"执行钩子 [{stage}]：{command}");
            var success = await ExecuteAsync(command, stage, targetRoot, ct).ConfigureAwait(false);
            if (success)
            {
                continue;
            }

            log.Error($"钩子失败 [{stage}]：{command}");
            if (abortPolicy)
            {
                return false;
            }

            log.Warning("hookFailPolicy=continue，继续后续步骤");
        }

        return true;
    }

    private async Task<bool> ExecuteAsync(string command, string stage, string targetRoot, CancellationToken ct)
    {
        var (fileName, arguments) = ResolveCommand(command, targetRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = targetRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.Environment["ATOMUPDATE_FROM_VERSION"] = task.FromVersion ?? "";
        startInfo.Environment["ATOMUPDATE_TO_VERSION"] = task.ToVersion ?? "";
        startInfo.Environment["ATOMUPDATE_TARGET"] = targetRoot;
        startInfo.Environment["ATOMUPDATE_PACKAGE"] = task.PackagePath ?? "";
        startInfo.Environment["ATOMUPDATE_STAGE"] = stage;
        startInfo.Environment["ATOMUPDATE_LOG_PATH"] = log.FilePath ?? "";
        startInfo.Environment["ATOMUPDATE_INSTALLATION_ID"] = task.InstallationId ?? "";
        if (log.FilePath is not null)
        {
            startInfo.Environment["ATOMUPDATE_LOG_DIR"] = Path.GetDirectoryName(log.FilePath)!;
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                log.Error($"钩子进程无法启动：{fileName}");
                return false;
            }

            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, task.Install.HookTimeoutSeconds)));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                log.Error($"钩子超时（{task.Install.HookTimeoutSeconds}s）：{command}");
                return false;
            }

            LogOutput(await stdout.ConfigureAwait(false));
            LogOutput(await stderr.ConfigureAwait(false));
            if (process.ExitCode != 0)
            {
                log.Error($"钩子退出码 {process.ExitCode}：{command}");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            log.Error($"钩子执行异常：{command}", exception);
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private void LogOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            log.Debug($"  [hook] {line.TrimEnd('\r')}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // 进程可能已退出
        }
    }

    /// <summary>命令解析：相对路径先按目标目录解析；按扩展名选择解释器。</summary>
    public static (string FileName, string Arguments) ResolveCommand(string command, string targetRoot)
    {
        var resolved = command;
        if (!Path.IsPathRooted(command))
        {
            var candidate = Path.Combine(targetRoot, command);
            if (File.Exists(candidate))
            {
                resolved = candidate;
            }
        }

        var extension = Path.GetExtension(resolved).ToLowerInvariant();
        return extension switch
        {
            ".bat" or ".cmd" => ("cmd", $"/c \"{resolved}\""),
            ".ps1" => ("powershell", $"-ExecutionPolicy Bypass -File \"{resolved}\""),
            ".sh" => ("/bin/sh", $"\"{resolved}\""),
            _ => (resolved, "")
        };
    }
}
