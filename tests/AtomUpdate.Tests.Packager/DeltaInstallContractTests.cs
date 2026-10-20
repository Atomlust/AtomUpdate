using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Delta;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Updater;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Installers;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Tests.Packager;

/// <summary>跨组件契约（docs/05 §7）：Packager 真实产物（FileDeltaGenerator）
/// 必须能被 Updater 真实消费方（ZipInstaller）安装——manifest.json/delete.json/files 布局、
/// camelCase 字段、SHA256/size、fromVersion 校验与删除清单生效，不靠手写 JSON。</summary>
public class DeltaInstallContractTests
{
    private static readonly UpdaterLog Silent = UpdaterLog.ConsoleOnly(UpdaterLogLevel.None);

    [Fact]
    public async Task GeneratedDelta_InstallsViaZipInstaller_ChangesAppliedAndDeletionsEffective()
    {
        using var workspace = new TestWorkspace();

        // 基线（= 已安装在用户机器上的 1.0.1）
        workspace.WriteFile("baseline/app.txt", "old");
        workspace.WriteFile("baseline/sub/lib.txt", "lib");
        workspace.WriteFile("baseline/removed.txt", "bye");

        // 当前版本 1.0.2：app.txt 修改、new.txt 新增、lib.txt 不动、removed.txt 删除
        workspace.WriteFile("current/app.txt", "v2");
        workspace.WriteFile("current/sub/lib.txt", "lib");
        workspace.WriteFile("current/new.txt", "fresh");

        var result = await new FileDeltaGenerator().GenerateAsync(new DeltaContext
        {
            ProjectKey = "demo",
            AppId = "app",
            FromVersion = "1.0.1",
            Version = "1.0.2",
            Platform = UpdatePlatform.Windows,
            Arch = UpdateArch.X64,
            BaselineDirectory = workspace.BaselineDirectory,
            CurrentDirectory = workspace.CurrentDirectory,
            OutputDirectory = workspace.OutputDirectory
        });

        // 增量结果统计与实际差异一致
        Assert.Equal(2, result.FileCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.True(File.Exists(result.PackagePath));

        // 模拟已安装 1.0.1 的目标目录
        var target = workspace.ToAbsolute("target");
        Directory.CreateDirectory(target);
        File.Copy(workspace.ToAbsolute("baseline/app.txt"), Path.Combine(target, "app.txt"));
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        File.Copy(workspace.ToAbsolute("baseline/sub/lib.txt"), Path.Combine(target, "sub", "lib.txt"));
        File.Copy(workspace.ToAbsolute("baseline/removed.txt"), Path.Combine(target, "removed.txt"));

        var task = new UpdaterTaskFile
        {
            AppId = "app",
            PackagePath = result.PackagePath,
            PackageType = PackageTypes.Zip,
            Kind = "delta",
            FromVersion = "1.0.1",
            ToVersion = "1.0.2",
            TargetPath = target,
            Install = new InstallSection { BackupPath = workspace.ToAbsolute("backup") }
        };

        var backup = await PackageInstallerFactory.Create(task, Silent)
            .InstallAsync(target, CancellationToken.None);

        Assert.NotNull(backup);
        Assert.Equal("v2", await File.ReadAllTextAsync(Path.Combine(target, "app.txt")));
        Assert.Equal("fresh", await File.ReadAllTextAsync(Path.Combine(target, "new.txt")));
        Assert.Equal("lib", await File.ReadAllTextAsync(Path.Combine(target, "sub", "lib.txt")));
        Assert.False(File.Exists(Path.Combine(target, "removed.txt")));
    }

    [Fact]
    public async Task GeneratedDelta_InstalledOnWrongBaseline_FailsVerification()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("baseline/app.txt", "old");
        workspace.WriteFile("current/app.txt", "v2");

        var result = await new FileDeltaGenerator().GenerateAsync(new DeltaContext
        {
            ProjectKey = "demo",
            AppId = "app",
            FromVersion = "1.0.1",
            Version = "1.0.2",
            Platform = UpdatePlatform.Windows,
            Arch = UpdateArch.X64,
            BaselineDirectory = workspace.BaselineDirectory,
            CurrentDirectory = workspace.CurrentDirectory,
            OutputDirectory = workspace.OutputDirectory
        });

        var target = workspace.ToAbsolute("target");
        Directory.CreateDirectory(target);
        var task = new UpdaterTaskFile
        {
            AppId = "app",
            PackagePath = result.PackagePath,
            PackageType = PackageTypes.Zip,
            Kind = "delta",
            FromVersion = "9.9.9",
            ToVersion = "1.0.2",
            TargetPath = target,
            Install = new InstallSection { BackupPath = workspace.ToAbsolute("backup") }
        };

        var exception = await Assert.ThrowsAsync<UpdaterFailureException>(() =>
            PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
        Assert.Contains("基线不匹配", exception.Message);
    }
}
