using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AtomUpdate.Updater;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Installers;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Tests.Updater;

public class ZipInstallerTests
{
    private static readonly UpdaterLog Silent = UpdaterLog.ConsoleOnly(UpdaterLogLevel.None);

    [Fact]
    public async Task Install_FullPackage_ExtractsToTarget()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var zip = workspace.Resolve("update.zip");
        CreateZip(zip, ("app.txt", "new"), ("sub/lib.txt", "lib"));
        var task = NewTask(workspace, zip);

        var manifest = await PackageInstallerFactory.Create(task, Silent)
            .InstallAsync(target, CancellationToken.None);

        Assert.NotNull(manifest);
        Assert.Equal("new", workspace.ReadFile("target/app.txt"));
        Assert.Equal("lib", workspace.ReadFile("target/sub/lib.txt"));
    }

    [Fact]
    public async Task Install_DeltaPackage_AutoDetectedWithDeletionsAndFileChecks()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/old.txt", "bye");
        workspace.WriteFile("target/keep.txt", "keep");

        var zip = workspace.Resolve("delta.zip");
        CreateZip(zip,
            ("files/app.txt", "v2"),
            ("manifest.json",
                $$"""{"fromVersion":"1.0.1","toVersion":"1.0.2","files":[{"path":"app.txt","hash":"{{Sha256Of("v2")}}","size":2}],"deleted":["old.txt"]}"""),
            ("delete.json", """["old.txt"]"""));
        var task = NewTask(workspace, zip);
        task.FromVersion = "1.0.1";

        await PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None);

        Assert.Equal("v2", workspace.ReadFile("target/app.txt"));
        Assert.False(File.Exists(workspace.Resolve("target", "old.txt")));
        Assert.Equal("keep", workspace.ReadFile("target/keep.txt"));
    }

    [Fact]
    public async Task Install_DeltaBaselineMismatch_FailsVerification()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var zip = workspace.Resolve("delta.zip");
        CreateZip(zip,
            ("files/app.txt", "v2"),
            ("manifest.json", """{"fromVersion":"1.0.1","toVersion":"1.0.2","files":[],"deleted":[]}"""),
            ("delete.json", "[]"));
        var task = NewTask(workspace, zip);
        task.FromVersion = "9.9.9";

        var exception = await Assert.ThrowsAsync<UpdaterFailureException>(() =>
            PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
        Assert.Contains("基线不匹配", exception.Message);
    }

    [Fact]
    public async Task Install_DeltaFileHashMismatch_FailsVerification()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var zip = workspace.Resolve("delta.zip");
        CreateZip(zip,
            ("files/app.txt", "v2"),
            ("manifest.json",
                $$"""{"fromVersion":"1.0.1","toVersion":"1.0.2","files":[{"path":"app.txt","hash":"{{new string('0', 64)}}","size":2}],"deleted":[]}"""),
            ("delete.json", "[]"));
        var task = NewTask(workspace, zip);
        task.FromVersion = "1.0.1";

        var exception = await Assert.ThrowsAsync<UpdaterFailureException>(() =>
            PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
        Assert.Contains("哈希不匹配", exception.Message);
    }

    [Fact]
    public async Task Install_ZipSlipEntry_Rejected()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var zip = workspace.Resolve("evil.zip");
        CreateZip(zip, ("../evil.txt", "pwned"));
        var task = NewTask(workspace, zip);

        var exception = await Assert.ThrowsAsync<UpdaterFailureException>(() =>
            PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None));

        Assert.Equal(ExitCodes.InstallerFailed, exception.ExitCode);
        Assert.False(File.Exists(workspace.Resolve("evil.txt")));
    }

    [Fact]
    public async Task Install_UnsupportedPackageType_Fails()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var zip = workspace.Resolve("update.zip");
        CreateZip(zip, ("app.txt", "new"));
        var task = NewTask(workspace, zip);
        task.PackageType = PackageTypes.Msi;

        var exception = await Assert.ThrowsAsync<UpdaterFailureException>(() =>
            PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None));

        Assert.Equal(ExitCodes.InstallerFailed, exception.ExitCode);
    }

    [Fact]
    public async Task Install_ExplicitDeltaKindWithoutManifest_Fails()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var zip = workspace.Resolve("delta.zip");
        CreateZip(zip, ("files/app.txt", "v2"));
        var task = NewTask(workspace, zip);
        task.Kind = "delta";
        task.FromVersion = "1.0.1";

        var exception = await Assert.ThrowsAsync<UpdaterFailureException>(() =>
            PackageInstallerFactory.Create(task, Silent).InstallAsync(target, CancellationToken.None));

        Assert.Equal(ExitCodes.InstallerFailed, exception.ExitCode);
    }

    private static UpdaterTaskFile NewTask(TestWorkspace workspace, string packagePath) =>
        new()
        {
            AppId = "test",
            PackagePath = packagePath,
            PackageType = PackageTypes.Zip,
            TargetPath = workspace.Resolve("target"),
            ToVersion = "1.0.2",
            Install = new InstallSection { BackupPath = workspace.Resolve("backup") }
        };

    private static string Sha256Of(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
