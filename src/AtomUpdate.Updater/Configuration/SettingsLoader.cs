using System.Text.Json;
using System.Text.Json.Nodes;
using AtomUpdate.Updater.Json;

namespace AtomUpdate.Updater.Configuration;

public sealed class SettingsLoadResult
{
    public required UpdaterTaskFile Task { get; init; }

    public required CliOptions Cli { get; init; }

    public required List<string> Errors { get; init; }
}

/// <summary>配置五层合并（04 §4.13）：内置默认 → 安装目录 updater.default.json → --config → ATOMUPDATE_UPDATER_CONFIG 文件 → 命令行。
/// verification 节锁定为前两层——任何运行期传入（--config/环境变量）都不参与合并，防同机攻击者注入降级校验。</summary>
public static class SettingsLoader
{
    public const string DefaultFileName = "updater.default.json";

    public const string EnvConfigVariable = "ATOMUPDATE_UPDATER_CONFIG";

    public static SettingsLoadResult Load(CliOptions cli, string installationDirectory,
        Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var errors = new List<string>();

        var defaults = (JsonObject)JsonSerializer.SerializeToNode(new UpdaterTaskFile(),
            UpdaterJsonContext.Default.UpdaterTaskFile)!;

        // 信任层：内置默认 + 安装目录 updater.default.json
        var trusted = (JsonObject)defaults.DeepClone();
        MergeFile(trusted, Path.Combine(installationDirectory, DefaultFileName), errors, required: false);

        // 运行层：--config → 环境变量指向的文件
        var merged = (JsonObject)trusted.DeepClone();
        if (!string.IsNullOrEmpty(cli.Config))
        {
            MergeFile(merged, cli.Config!, errors, required: true);
        }

        var envPath = env(EnvConfigVariable);
        if (!string.IsNullOrEmpty(envPath))
        {
            MergeFile(merged, envPath!, errors, required: true);
        }

        // verification 锁定回信任层内容
        merged["verification"] = trusted["verification"]?.DeepClone();

        UpdaterTaskFile task;
        try
        {
            task = merged.Deserialize(UpdaterJsonContext.Default.UpdaterTaskFile) ?? new UpdaterTaskFile();
        }
        catch (JsonException exception)
        {
            errors.Add($"配置解析失败：{exception.Message}");
            task = new UpdaterTaskFile();
        }

        ApplyCli(task, cli);
        return new SettingsLoadResult { Task = task, Cli = cli, Errors = errors };
    }

    private static void MergeFile(JsonObject target, string path, List<string> errors, bool required)
    {
        if (!File.Exists(path))
        {
            if (required)
            {
                errors.Add($"配置文件不存在：{path}");
            }

            return;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject obj)
            {
                Merge(target, obj);
            }
            else
            {
                errors.Add($"配置文件不是 JSON 对象：{path}");
            }
        }
        catch (Exception exception)
        {
            errors.Add($"配置文件读取失败：{path}（{exception.Message}）");
        }
    }

    /// <summary>递归对象合并：对象逐键合并，标量/数组整体替换。</summary>
    public static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is JsonObject sourceChild && target[pair.Key] is JsonObject targetChild)
            {
                Merge(targetChild, sourceChild);
            }
            else
            {
                target[pair.Key] = pair.Value?.DeepClone();
            }
        }
    }

    private static void ApplyCli(UpdaterTaskFile task, CliOptions cli)
    {
        if (cli.Package is not null) task.PackagePath = cli.Package;
        if (cli.Target is not null) task.TargetPath = cli.Target;
        if (cli.FromVersion is not null) task.FromVersion = cli.FromVersion;
        if (cli.ToVersion is not null) task.ToVersion = cli.ToVersion;
        if (cli.Sha256 is not null) task.Sha256 = cli.Sha256;
        if (cli.Signature is not null) task.Signature = cli.Signature;
        if (cli.KeyId is not null) task.KeyId = cli.KeyId;
        if (cli.PackageType is not null) task.PackageType = cli.PackageType;
        if (cli.InstallMode is not null) task.InstallMode = cli.InstallMode;
        if (cli.ServerUrl is not null) task.ServerUrl = cli.ServerUrl;
        if (cli.InstallationId is not null) task.InstallationId = cli.InstallationId;
        if (cli.MachineFingerprint is not null) task.MachineFingerprint = cli.MachineFingerprint;
        if (cli.AppId is not null) task.AppId = cli.AppId;
        if (cli.ProjectKey is not null) task.ProjectKey = cli.ProjectKey;
        if (cli.Restart is not null) task.RestartPath = cli.Restart;
        if (cli.RestartArgs is not null) task.RestartArgs = cli.RestartArgs;
        if (cli.InstallArgs is not null) task.InstallArgs = cli.InstallArgs;
        if (cli.LogLevel is not null) task.Updater.LogLevel = cli.LogLevel;
        if (cli.LogPath is not null) task.Updater.LogPath = cli.LogPath;
        if (cli.Verbose) task.Updater.LogLevel = "debug";
        if (cli.DryRun) task.Updater.DryRun = true;
        if (cli.NoRestart) task.Restart.Enabled = false;
        if (cli.NoRollback) task.Rollback.Enabled = false;
        task.InstallMode ??= InstallModes.OnRestart;
    }
}
