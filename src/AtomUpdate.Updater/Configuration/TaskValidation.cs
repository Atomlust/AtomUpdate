namespace AtomUpdate.Updater.Configuration;

/// <summary>参数校验（04 §4.3）：必填/枚举/路径。任何错误 → 0x1002。</summary>
public static class TaskValidation
{
    public static readonly string[] LogLevels =
        ["trace", "debug", "information", "info", "warning", "warn", "error", "critical", "none"];

    public static List<string> Validate(UpdaterTaskFile task)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(task.PackagePath))
        {
            errors.Add("缺少必填参数：--package（更新包路径）");
        }
        else if (!File.Exists(task.PackagePath))
        {
            errors.Add($"更新包不存在：{task.PackagePath}");
        }

        if (string.IsNullOrWhiteSpace(task.TargetPath))
        {
            errors.Add("缺少必填参数：--target（目标目录）");
        }
        else if (!Directory.Exists(task.TargetPath))
        {
            errors.Add($"目标目录不存在：{task.TargetPath}");
        }

        if (string.IsNullOrWhiteSpace(task.ToVersion))
        {
            errors.Add("缺少必填参数：--to-version");
        }

        if (string.IsNullOrWhiteSpace(task.PackageType) || !PackageTypes.IsValid(task.PackageType))
        {
            errors.Add($"--package-type 无效（应为 {string.Join('|', PackageTypes.All)}）：{task.PackageType}");
        }

        if (!InstallModes.IsValid(task.InstallMode ?? ""))
        {
            errors.Add($"--install-mode 无效（应为 {string.Join('|', InstallModes.All)}）：{task.InstallMode}");
        }

        if (!string.Equals(task.Install.Strategy, ReplaceStrategies.Merge, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(task.Install.Strategy, ReplaceStrategies.Replace, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"install.strategy 无效（应为 merge|replace）：{task.Install.Strategy}");
        }

        if (!string.Equals(task.Install.HookFailPolicy, HookFailPolicies.Abort, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(task.Install.HookFailPolicy, HookFailPolicies.Continue, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"install.hookFailPolicy 无效（应为 abort|continue）：{task.Install.HookFailPolicy}");
        }

        if (!ElevationModes.IsValid(task.Elevation.Mode))
        {
            errors.Add($"elevation.mode 无效（应为 onDemand|always|never）：{task.Elevation.Mode}");
        }

        if (!LogLevels.Contains(task.Updater.LogLevel, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"updater.logLevel 无效（应为 {string.Join('|', LogLevels)}）：{task.Updater.LogLevel}");
        }

        if (task.Updater.MutexTimeoutSeconds < 0) errors.Add("updater.mutexTimeout 不能为负");
        if (task.Updater.LogRetentionDays < 0) errors.Add("updater.logRetentionDays 不能为负");
        if (task.Install.BackupRetentionDays < 0) errors.Add("install.backupRetentionDays 不能为负");
        if (task.Install.ReplaceRetryCount < 0) errors.Add("install.replaceRetryCount 不能为负");
        if (task.Install.ReplaceRetryDelayMs < 0) errors.Add("install.replaceRetryDelayMs 不能为负");
        if (task.Install.HookTimeoutSeconds < 0) errors.Add("install.hookTimeoutSeconds 不能为负");
        if (task.Restart.DelayMs < 0) errors.Add("restart.delayMs 不能为负");
        if (task.Restart.WaitForExitMs < 0) errors.Add("restart.waitForExitMs 不能为负");
        if (task.Report.TimeoutSeconds < 0) errors.Add("report.timeoutSeconds 不能为负");
        if (task.Report.RetryCount < 0) errors.Add("report.retryCount 不能为负");
        if (task.Network.TimeoutSeconds < 0) errors.Add("network.timeoutSeconds 不能为负");
        if (task.Network.RetryCount < 0) errors.Add("network.retryCount 不能为负");

        if (task.Kind is { } kind &&
            !string.Equals(kind, "full", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(kind, "delta", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"kind 无效（应为 full|delta）：{task.Kind}");
        }

        if (task.Elevation is { Retry: var retry } &&
            !string.Equals(retry, "unlimited", StringComparison.OrdinalIgnoreCase) &&
            !int.TryParse(retry, out _))
        {
            errors.Add($"elevation.retry 无效（应为 unlimited 或数字）：{retry}");
        }

        return errors;
    }
}
