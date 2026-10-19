using AtomUpdate.Updater.Configuration;

namespace AtomUpdate.Tests.Updater;

public class TaskValidationTests
{
    [Fact]
    public void Validate_EmptyTask_ReportsRequiredFields()
    {
        var errors = TaskValidation.Validate(new UpdaterTaskFile());

        Assert.Contains(errors, error => error.Contains("--package"));
        Assert.Contains(errors, error => error.Contains("--target"));
        Assert.Contains(errors, error => error.Contains("--to-version"));
        Assert.Contains(errors, error => error.Contains("--package-type"));
    }

    [Fact]
    public void Validate_MissingPackageFile_Reported()
    {
        using var workspace = new TestWorkspace();
        var task = ValidTask(workspace);
        task.PackagePath = workspace.Resolve("missing.zip");

        var errors = TaskValidation.Validate(task);

        Assert.Contains(errors, error => error.Contains("更新包不存在"));
    }

    [Fact]
    public void Validate_MissingTargetDirectory_Reported()
    {
        using var workspace = new TestWorkspace();
        var task = ValidTask(workspace);
        task.TargetPath = workspace.Resolve("missing-target");

        var errors = TaskValidation.Validate(task);

        Assert.Contains(errors, error => error.Contains("目标目录不存在"));
    }

    [Fact]
    public void Validate_InvalidEnums_Reported()
    {
        using var workspace = new TestWorkspace();
        var task = ValidTask(workspace);
        task.PackageType = "exe-custom";
        task.InstallMode = "whenever";
        task.Install.Strategy = "overwrite";
        task.Install.HookFailPolicy = "ignore";
        task.Elevation.Mode = "maybe";
        task.Updater.LogLevel = "loud";
        task.Kind = "patch";

        var errors = TaskValidation.Validate(task);

        Assert.Contains(errors, error => error.Contains("--package-type 无效"));
        Assert.Contains(errors, error => error.Contains("--install-mode 无效"));
        Assert.Contains(errors, error => error.Contains("install.strategy 无效"));
        Assert.Contains(errors, error => error.Contains("install.hookFailPolicy 无效"));
        Assert.Contains(errors, error => error.Contains("elevation.mode 无效"));
        Assert.Contains(errors, error => error.Contains("updater.logLevel 无效"));
        Assert.Contains(errors, error => error.Contains("kind 无效"));
    }

    [Fact]
    public void Validate_NegativeNumbers_Reported()
    {
        using var workspace = new TestWorkspace();
        var task = ValidTask(workspace);
        task.Updater.MutexTimeoutSeconds = -1;
        task.Install.ReplaceRetryCount = -1;
        task.Restart.DelayMs = -1;
        task.Report.RetryCount = -1;

        var errors = TaskValidation.Validate(task);

        Assert.Contains(errors, error => error.Contains("mutexTimeout"));
        Assert.Contains(errors, error => error.Contains("replaceRetryCount"));
        Assert.Contains(errors, error => error.Contains("restart.delayMs"));
        Assert.Contains(errors, error => error.Contains("report.retryCount"));
    }

    [Fact]
    public void Validate_ValidTask_NoErrors()
    {
        using var workspace = new TestWorkspace();
        Assert.Empty(TaskValidation.Validate(ValidTask(workspace)));
    }

    private static UpdaterTaskFile ValidTask(TestWorkspace workspace)
    {
        workspace.WriteFile("package.zip", "zip");
        workspace.WriteFile("target/app.txt", "app");
        return new UpdaterTaskFile
        {
            PackagePath = workspace.Resolve("package.zip"),
            TargetPath = workspace.Resolve("target"),
            FromVersion = "1.0.0",
            ToVersion = "1.0.1",
            PackageType = PackageTypes.Zip,
            InstallMode = InstallModes.OnRestart
        };
    }
}
