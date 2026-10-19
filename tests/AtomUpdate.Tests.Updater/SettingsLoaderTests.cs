using AtomUpdate.Updater.Configuration;

namespace AtomUpdate.Tests.Updater;

public class SettingsLoaderTests
{
    [Fact]
    public void Load_BuiltInDefaults()
    {
        using var workspace = new TestWorkspace();

        var result = SettingsLoader.Load(new CliOptions(), workspace.Root);

        Assert.Empty(result.Errors);
        Assert.Equal("information", result.Task.Updater.LogLevel);
        Assert.Equal(7, result.Task.Updater.LogRetentionDays);
        Assert.Equal("merge", result.Task.Install.Strategy);
        Assert.True(result.Task.Install.BackupBeforeReplace);
        Assert.Equal(3, result.Task.Install.BackupRetentionDays);
        Assert.Equal("onDemand", result.Task.Elevation.Mode);
        Assert.Equal("unlimited", result.Task.Elevation.Retry);
        Assert.Equal("onrestart", result.Task.InstallMode);
        Assert.Equal("--skip-update-check", result.Task.Restart.Args);
        Assert.Equal(15, result.Task.Report.TimeoutSeconds);
        Assert.Equal(3, result.Task.Report.RetryCount);
        Assert.True(result.Task.Report.FallbackToLocal);
    }

    [Fact]
    public void Load_ConfigOverridesInstallDefaults_VerificationLockedToTrustedLayers()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(workspace.Resolve("updater.default.json"),
            """{"updater":{"logLevel":"debug"},"verification":{"requireSignature":true,"publicKeys":{"k1":"PEM1"}}}""");
        var configPath = workspace.Resolve("task.json");
        File.WriteAllText(configPath,
            """{"updater":{"logLevel":"warning"},"verification":{"requireSignature":false,"publicKeys":{"k2":"PEM2"}}}""");

        var result = SettingsLoader.Load(new CliOptions { Config = configPath }, workspace.Root);

        Assert.Empty(result.Errors);
        Assert.Equal("warning", result.Task.Updater.LogLevel);
        Assert.True(result.Task.Verification.RequireSignature);
        Assert.True(result.Task.Verification.PublicKeys.ContainsKey("k1"));
        Assert.False(result.Task.Verification.PublicKeys.ContainsKey("k2"));
    }

    [Fact]
    public void Load_EnvFileOverridesConfigFile()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.Resolve("task.json");
        File.WriteAllText(configPath, """{"updater":{"logLevel":"debug"}}""");
        var envPath = workspace.Resolve("env.json");
        File.WriteAllText(envPath, """{"updater":{"logLevel":"error"}}""");

        var result = SettingsLoader.Load(new CliOptions { Config = configPath }, workspace.Root,
            name => name == SettingsLoader.EnvConfigVariable ? envPath : null);

        Assert.Empty(result.Errors);
        Assert.Equal("error", result.Task.Updater.LogLevel);
    }

    [Fact]
    public void Load_CliOverridesEverything()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.Resolve("task.json");
        File.WriteAllText(configPath,
            """{"updater":{"logLevel":"debug"},"restart":{"enabled":true},"rollback":{"enabled":true}}""");

        var result = SettingsLoader.Load(new CliOptions
        {
            Config = configPath,
            LogLevel = "trace",
            NoRestart = true,
            NoRollback = true,
            DryRun = true
        }, workspace.Root);

        Assert.Empty(result.Errors);
        Assert.Equal("trace", result.Task.Updater.LogLevel);
        Assert.False(result.Task.Restart.Enabled);
        Assert.False(result.Task.Rollback.Enabled);
        Assert.True(result.Task.Updater.DryRun);
    }

    [Fact]
    public void Load_VerboseMapsToDebug()
    {
        using var workspace = new TestWorkspace();

        var result = SettingsLoader.Load(new CliOptions { Verbose = true }, workspace.Root);

        Assert.Equal("debug", result.Task.Updater.LogLevel);
    }

    [Fact]
    public void Load_RuntimeFieldsFromConfig()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.Resolve("task.json");
        File.WriteAllText(configPath,
            """{"packagePath":"a.zip","targetPath":"/app","fromVersion":"1.0.0","toVersion":"1.0.1","packageType":"zip","appId":"demo"}""");

        var result = SettingsLoader.Load(new CliOptions { Config = configPath }, workspace.Root);

        Assert.Empty(result.Errors);
        Assert.Equal("a.zip", result.Task.PackagePath);
        Assert.Equal("/app", result.Task.TargetPath);
        Assert.Equal("1.0.1", result.Task.ToVersion);
        Assert.Equal("demo", result.Task.AppId);
    }

    [Fact]
    public void Load_MissingRequiredConfigFile_ReportsError()
    {
        using var workspace = new TestWorkspace();

        var result = SettingsLoader.Load(new CliOptions { Config = workspace.Resolve("missing.json") }, workspace.Root);

        Assert.Contains(result.Errors, error => error.Contains("配置文件不存在"));
    }

    [Fact]
    public void Load_MissingInstallDefaultFile_IsNotAnError()
    {
        using var workspace = new TestWorkspace();

        var result = SettingsLoader.Load(new CliOptions(), workspace.Root);

        Assert.Empty(result.Errors);
    }
}
