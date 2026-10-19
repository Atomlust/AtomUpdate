using AtomUpdate.Updater.Cli;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Elevation;
using AtomUpdate.Updater.Hooks;
using AtomUpdate.Updater.Installers;
using AtomUpdate.Updater.Locking;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Report;
using AtomUpdate.Updater.Replace;
using AtomUpdate.Updater.Restart;
using AtomUpdate.Updater.SelfUpdate;
using AtomUpdate.Updater.State;
using AtomUpdate.Updater.Verification;

namespace AtomUpdate.Updater;

/// <summary>更新器主流程（04 §4.5）：
/// 解析参数 → 加载配置（五层合并）→ 初始化日志 → 自替换（--replace-old）→ 互斥 → 校验参数 →
/// 校验包（SHA256/签名）→ dry-run 退出 → 自升级交接 → 提权 → 等主程序退出 → 钩子+安装 →
/// 成功状态/回传 → 重启 → 清理备份。
/// 顺序偏差（相对文档 §4.5，按强度优先调整）：①参数/包校验在自升级交接之前（保证验签强度）；
/// ②dry-run 在交接/提权之前退出；③钩子为 preInstall → preReplace →（备份+替换）→ postReplace → postInstall（语义等价）。</summary>
public static class UpdaterApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var (parsed, _) = CliArguments.Parse(args);
        try
        {
            return await RunCoreAsync(args).ConfigureAwait(false);
        }
        finally
        {
            // 启动配置含 apiKey 明文，装完（含失败/提权回环）即清理，不在 %Temp% 残留（04 §5.1）
            TryDeleteLaunchConfig(parsed.Config);
        }
    }

    /// <summary>只删本组件写入的临时启动配置（%Temp%\atomupdate\updater-*.json 与 selfupdate\handoff-*.json）；
    /// 用户手写的 --config 路径保留。提权回环时子进程先删，父进程的删除静默跳过。</summary>
    public static bool TryDeleteLaunchConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(configPath);
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "atomupdate"))
                + Path.DirectorySeparatorChar;
            if (!full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var name = Path.GetFileName(full);
            var folder = Path.GetFileName(Path.GetDirectoryName(full));
            var isSdkConfig = name.StartsWith("updater-", StringComparison.OrdinalIgnoreCase) &&
                              full.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            var isHandoff = name.StartsWith("handoff-", StringComparison.OrdinalIgnoreCase) &&
                            folder == "selfupdate";
            if (!isSdkConfig && !isHandoff)
            {
                return false;
            }

            if (!File.Exists(full))
            {
                return false;
            }

            File.Delete(full);
            return true;
        }
        catch (Exception)
        {
            // 清理失败不影响安装结果
            return false;
        }
    }

    private static async Task<int> RunCoreAsync(string[] args)
    {
        var (cli, cliErrors) = CliArguments.Parse(args);
        if (cli.Help)
        {
            Console.WriteLine(CliArguments.Usage);
            return ExitCodes.Success;
        }

        var settings = SettingsLoader.Load(cli, cli.Target ?? "");
        var task = settings.Task;
        var log = UpdaterLog.Create(task.Updater.LogLevel, task.Updater.LogPath, task.Updater.LogRetentionDays);
        log.Information($"AtomUpdate.Updater {SelfUpdater.CurrentVersion} 启动（PID {Environment.ProcessId}）");

        if (cliErrors.Count > 0 || settings.Errors.Count > 0)
        {
            foreach (var error in cliErrors)
            {
                log.Error(error);
            }

            foreach (var error in settings.Errors)
            {
                log.Error(error);
            }

            return ExitCodes.ArgumentError;
        }

        // 双进程自升级的替换端（04 §4.12）：替换旧更新器文件，失败仅记日志继续原任务
        if (!string.IsNullOrWhiteSpace(cli.ReplaceOld))
        {
            SelfReplacer.Replace(cli.ReplaceOld!, cli.ReplacePid, log);
        }

        var store = new StateStore(task.AppId ?? "");
        var previous = store.Load();
        var retryCount = previous is not null &&
                         previous.FromVersion == task.FromVersion &&
                         previous.ToVersion == task.ToVersion
            ? previous.RetryCount + 1
            : 0;

        // 全局互斥（04 §4.11，按 appId；失败 0x1001 不提示用户）
        GlobalMutex? mutex = null;
        if (task.Updater.GlobalMutex)
        {
            mutex = GlobalMutex.TryAcquire(task.AppId ?? "", task.Updater.MutexTimeoutSeconds, log, out var reason);
            if (mutex is null)
            {
                log.Error($"互斥获取失败（0x1001）：{reason}");
                return ExitCodes.MutexAcquireFailed;
            }
        }

        using var mutexScope = mutex;

        var validationErrors = TaskValidation.Validate(task);
        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                log.Error(error);
            }

            return ExitCodes.ArgumentError;
        }

        // dry-run：只校验（含包哈希/签名），不产生状态/替换/提权/回传/重启
        if (task.Updater.DryRun)
        {
            PackageVerifier.Verify(task, log);
            log.Information($"dry-run：校验通过（package={task.PackagePath}，target={task.TargetPath}，type={task.PackageType}）");
            return ExitCodes.Success;
        }

        var state = new UpdaterState
        {
            InstallationId = task.InstallationId,
            MachineFingerprint = task.MachineFingerprint,
            ProjectKey = task.ProjectKey,
            AppId = task.AppId,
            FromVersion = task.FromVersion,
            ToVersion = task.ToVersion,
            Stage = StateStages.Starting,
            InstallMode = task.InstallMode,
            Elevated = ElevationService.IsElevated(),
            LogPath = log.FilePath,
            RetryCount = retryCount,
            UpdaterVersion = SelfUpdater.CurrentVersion
        };
        store.Save(state);

        try
        {
            // 校验更新包 SHA256 / 签名（0x1004）
            state.Stage = StateStages.Verifying;
            state.Timestamp = DateTimeOffset.UtcNow;
            store.Save(state);
            PackageVerifier.Verify(task, log);

            // 自升级检查与交接（04 §4.12；--replace-old 模式跳过防递归；检查失败跳过继续原任务，Q16）
            if (string.IsNullOrWhiteSpace(cli.ReplaceOld) &&
                await new SelfUpdater(task, log).TryHandoffAsync().ConfigureAwait(false))
            {
                log.Information("已交接给新更新器，本进程退出");
                return ExitCodes.Success;
            }

            // 提权预判与自我提权（04 §4.10；--elevated 防重复弹窗；被拒/取消 → 0x1003）
            if (!cli.Elevated && ElevationService.NeedsElevation(task, task.TargetPath!, log))
            {
                var exitCode = ElevationService.ElevateAndWait(args, task, log);
                log.Information($"提权子进程退出码：0x{exitCode:X4}（镜像返回）");
                return exitCode;
            }

            state.Elevated = ElevationService.IsElevated();
            state.Stage = StateStages.Installing;
            state.Timestamp = DateTimeOffset.UtcNow;
            store.Save(state);

            // 等待主程序退出（04 §5.2；超时强制继续）
            var waitMs = task.WaitForMainProcessExitSeconds is { } seconds
                ? seconds * 1000
                : task.Restart.WaitForExitMs;
            RestartManager.WaitForMainProcessExit(task, waitMs, log);

            // 钩子 + 安装（备份包含在 zip 替换链路内）
            var hooks = new HookRunner(task, log);
            var targetRoot = task.TargetPath!;

            if (!await hooks.RunAsync(HookStages.PreInstall, targetRoot).ConfigureAwait(false))
            {
                throw new UpdaterFailureException(ExitCodes.HookFailed, "preInstall 钩子失败（hookFailPolicy=abort）");
            }

            if (!await hooks.RunAsync(HookStages.PreReplace, targetRoot).ConfigureAwait(false))
            {
                throw new UpdaterFailureException(ExitCodes.HookFailed, "preReplace 钩子失败（hookFailPolicy=abort）");
            }

            var installer = PackageInstallerFactory.Create(task, log);
            var manifest = await installer.InstallAsync(targetRoot, CancellationToken.None).ConfigureAwait(false);

            if (!await hooks.RunAsync(HookStages.PostReplace, targetRoot).ConfigureAwait(false))
            {
                throw new UpdaterFailureException(ExitCodes.HookFailed, "postReplace 钩子失败（hookFailPolicy=abort）")
                {
                    RollbackManifest = manifest
                };
            }

            if (!await hooks.RunAsync(HookStages.PostInstall, targetRoot).ConfigureAwait(false))
            {
                throw new UpdaterFailureException(ExitCodes.HookFailed, "postInstall 钩子失败（hookFailPolicy=abort）")
                {
                    RollbackManifest = manifest
                };
            }

            // 成功状态 + 回传（失败保持 reported=false 等 SDK 补报）
            state.Stage = StateStages.Completed;
            state.Result = StateResults.Success;
            state.Elevated = ElevationService.IsElevated();
            state.Timestamp = DateTimeOffset.UtcNow;
            store.Save(state);
            await ReportAsync(task, state, store, log).ConfigureAwait(false);

            // 重启主程序（04 §5.2；失败不回退安装结果，0x100A）
            if (task.Restart.Enabled)
            {
                if (!await hooks.RunAsync(HookStages.PreRestart, targetRoot).ConfigureAwait(false))
                {
                    log.Error("preRestart 钩子失败，跳过重启（安装已完成）");
                    return ExitCodes.HookFailed;
                }

                RestartManager.Restart(task, log);

                if (!await hooks.RunAsync(HookStages.PostRestart, targetRoot).ConfigureAwait(false))
                {
                    log.Error("postRestart 钩子失败（主程序已启动）");
                    return ExitCodes.HookFailed;
                }
            }
            else
            {
                log.Debug("restart.enabled=false，跳过重启");
            }

            Cleanup(task, manifest, log);
            log.Information("更新安装完成（0x0000）");
            return ExitCodes.Success;
        }
        catch (UpdaterFailureException failure)
        {
            return await FailAsync(task, state, store, log, failure.ExitCode, failure.Message, failure.RollbackManifest)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Error("安装过程未预期异常", exception);
            return await FailAsync(task, state, store, log, ExitCodes.Unknown, exception.Message, null)
                .ConfigureAwait(false);
        }
    }

    /// <summary>失败处理：可选回滚（rollback.enabled && onFailure && 有回滚清单）→ 写终态 → 回传。</summary>
    private static async Task<int> FailAsync(UpdaterTaskFile task, UpdaterState state, StateStore store,
        UpdaterLog log, int exitCode, string message, BackupManifest? rollbackManifest)
    {
        log.Error($"安装失败（0x{exitCode:X4}）：{message}");
        state.ErrorCode = $"0x{exitCode:X4}";
        state.ErrorMessage = message;
        state.Reason = message;
        if (exitCode == ExitCodes.ElevationRejected)
        {
            state.ElevationRejected = true;
        }

        var finalCode = exitCode;
        if (rollbackManifest is not null && task.Rollback.Enabled && task.Rollback.OnFailure)
        {
            log.Warning("开始回滚（rollback.onFailure）");
            var restored = RollbackManager.Restore(task.TargetPath!, rollbackManifest,
                task.Install.ReplaceRetryCount, task.Install.ReplaceRetryDelayMs, log);
            if (restored)
            {
                state.Stage = StateStages.RolledBack;
                state.Result = StateResults.RolledBack;
                log.Information("回滚完成");
            }
            else
            {
                state.Stage = StateStages.Failed;
                state.Result = StateResults.Failed;
                state.ErrorCode = $"0x{ExitCodes.RollbackFailed:X4}";
                state.ErrorMessage = $"回滚失败，需手动恢复（备份目录：{rollbackManifest.BackupDirectory}）；原始错误：{message}";
                finalCode = ExitCodes.RollbackFailed;
                log.Error("回滚失败（0x1009），需手动恢复");
            }
        }
        else
        {
            state.Stage = StateStages.Failed;
            state.Result = StateResults.Failed;
        }

        state.Timestamp = DateTimeOffset.UtcNow;
        store.Save(state);
        await ReportAsync(task, state, store, log).ConfigureAwait(false);
        return finalCode;
    }

    private static async Task ReportAsync(UpdaterTaskFile task, UpdaterState state, StateStore store, UpdaterLog log)
    {
        var report = new InstallReport
        {
            ProjectKey = state.ProjectKey,
            AppId = state.AppId,
            InstallationId = state.InstallationId,
            MachineFingerprint = state.MachineFingerprint,
            FromVersion = state.FromVersion,
            ToVersion = state.ToVersion,
            Stage = state.Stage,
            InstallMode = state.InstallMode,
            Elevated = state.Elevated,
            ElevationRejected = state.ElevationRejected,
            ErrorCode = state.ErrorCode,
            ErrorMessage = state.ErrorMessage,
            LogPath = state.LogPath,
            Timestamp = state.Timestamp
        };

        if (!task.Report.Enabled)
        {
            log.Debug("report.enabled=false，跳过回传（reported 记 true，不再由 SDK 补报）");
            state.Reported = true;
        }
        else
        {
            state.Reported = await new ReportClient(task, log).SendAsync(report).ConfigureAwait(false);
            if (!state.Reported)
            {
                log.Warning("回传未送达，状态保留 reported=false 等 SDK 补报");
            }
        }

        store.Save(state);
    }

    /// <summary>成功后清理：keepBackupOnSuccess=false → 删除本次备份；true → 保留并按 backupRetentionDays 清理过期备份。</summary>
    private static void Cleanup(UpdaterTaskFile task, BackupManifest? manifest, UpdaterLog log)
    {
        if (manifest?.BackupDirectory is not { } backupDirectory)
        {
            return;
        }

        if (!task.Rollback.KeepBackupOnSuccess)
        {
            TryDeleteDirectory(backupDirectory, log);
            log.Debug($"已删除安装备份：{backupDirectory}");
            return;
        }

        var parent = Path.GetDirectoryName(backupDirectory);
        if (parent is null || task.Install.BackupRetentionDays <= 0)
        {
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-task.Install.BackupRetentionDays);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(parent))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < threshold)
                    {
                        Directory.Delete(directory, recursive: true);
                        log.Debug($"已清理过期备份：{directory}");
                    }
                }
                catch (Exception exception)
                {
                    log.Debug($"清理过期备份失败：{directory}（{exception.Message}）");
                }
            }
        }
        catch (Exception exception)
        {
            log.Debug($"备份目录扫描失败：{exception.Message}");
        }
    }

    private static void TryDeleteDirectory(string path, UpdaterLog log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            log.Debug($"删除备份目录失败（保留供诊断）：{exception.Message}");
        }
    }
}
