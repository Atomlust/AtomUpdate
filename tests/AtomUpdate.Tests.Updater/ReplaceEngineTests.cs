using AtomUpdate.Updater;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Replace;

namespace AtomUpdate.Tests.Updater;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("*.txt", "a.txt", true)]
    [InlineData("*.txt", "sub/a.txt", false)]
    [InlineData("**/*.log", "a.log", true)]
    [InlineData("**/*.log", "x/y/a.log", true)]
    [InlineData("lib/**", "lib/a.dll", true)]
    [InlineData("lib/**", "lib/x/b.dll", true)]
    [InlineData("lib/**", "other/a.dll", false)]
    [InlineData("??.txt", "ab.txt", true)]
    [InlineData("??.txt", "abc.txt", false)]
    [InlineData("data/*.json", "data/a.json", true)]
    [InlineData("data/*.json", "data/x/a.json", false)]
    [InlineData("exact/file.txt", "exact/file.txt", true)]
    [InlineData("exact/file.txt", "exact/other.txt", false)]
    public void IsMatch_Cases(string pattern, string path, bool expected) =>
        Assert.Equal(expected, GlobMatcher.IsMatch(pattern, path, caseSensitive: true));

    [Fact]
    public void IsMatch_CaseInsensitiveWhenRequested() =>
        Assert.True(GlobMatcher.IsMatch("*.TXT", "a.txt", caseSensitive: false));

    [Fact]
    public void IsMatchAny_SkipsBlankPatterns() =>
        Assert.True(GlobMatcher.IsMatchAny(["", "  ", "*.txt"], "a.txt", caseSensitive: true));
}

public class FileReplacerTests
{
    private static readonly UpdaterLog Silent = UpdaterLog.ConsoleOnly(UpdaterLogLevel.None);

    [Fact]
    public void Apply_Merge_ReplacesAndKeepsOrphans()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("source/fresh.txt", "added");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/keep.txt", "keep");

        var manifest = new FileReplacer(NewTask(workspace), Silent).Apply(source, target);

        Assert.Equal("new", workspace.ReadFile("target/app.txt"));
        Assert.Equal("added", workspace.ReadFile("target/fresh.txt"));
        Assert.Equal("keep", workspace.ReadFile("target/keep.txt"));
        Assert.Contains("app.txt", manifest.BackedUpFiles);
        Assert.Contains("fresh.txt", manifest.CreatedFiles);
        Assert.NotNull(manifest.BackupDirectory);
        Assert.Equal("old", File.ReadAllText(Path.Combine(manifest.BackupDirectory!, "app.txt")));
    }

    [Fact]
    public void Apply_ReplaceStrategy_DeletesOrphans()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/orphan.txt", "gone");

        new FileReplacer(NewTask(workspace, strategy: ReplaceStrategies.Replace), Silent).Apply(source, target);

        Assert.Equal("new", workspace.ReadFile("target/app.txt"));
        Assert.False(File.Exists(workspace.Resolve("target", "orphan.txt")));
    }

    [Fact]
    public void Apply_DeleteOrphansFlag_DeletesInMergeMode()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/orphan.txt", "gone");

        new FileReplacer(NewTask(workspace, deleteOrphans: true), Silent).Apply(source, target);

        Assert.False(File.Exists(workspace.Resolve("target", "orphan.txt")));
    }

    [Fact]
    public void Apply_Exclude_SkipsReplaceAndDelete()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("source/secret/key.txt", "package");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/secret/key.txt", "mine");

        new FileReplacer(NewTask(workspace, strategy: ReplaceStrategies.Replace, exclude: ["secret/**"]), Silent)
            .Apply(source, target);

        Assert.Equal("new", workspace.ReadFile("target/app.txt"));
        Assert.Equal("mine", workspace.ReadFile("target/secret/key.txt"));
    }

    [Fact]
    public void Apply_Preserve_KeepsFileAgainstDeletion()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/license.key", "user");

        new FileReplacer(NewTask(workspace, strategy: ReplaceStrategies.Replace, preserve: ["license.key"]), Silent)
            .Apply(source, target);

        Assert.Equal("user", workspace.ReadFile("target/license.key"));
    }

    [Fact]
    public void Apply_Deletions_AppliedAndRespectExcludeAndReplaceSet()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "v2");
        workspace.WriteFile("target/app.txt", "v1");
        workspace.WriteFile("target/old.txt", "bye");
        workspace.WriteFile("target/protected.txt", "mine");

        new FileReplacer(NewTask(workspace, exclude: ["protected.txt"]), Silent)
            .Apply(source, target, ["old.txt", "protected.txt", "app.txt"]);

        Assert.False(File.Exists(workspace.Resolve("target", "old.txt")));
        Assert.Equal("mine", workspace.ReadFile("target/protected.txt"));
        Assert.Equal("v2", workspace.ReadFile("target/app.txt"));
    }

    [Fact]
    public void Apply_DeletionEscapingTarget_Rejected()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("outside.txt", "safe");

        var exception = Assert.Throws<UpdaterFailureException>(() =>
            new FileReplacer(NewTask(workspace), Silent).Apply(source, target, ["../outside.txt"]));

        Assert.Equal(ExitCodes.ReplaceFailed, exception.ExitCode);
        Assert.Equal("safe", workspace.ReadFile("outside.txt"));
    }

    [Fact]
    public void Apply_ReadOnlyFile_Replaced()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("target/app.txt", "old");
        var path = workspace.Resolve("target", "app.txt");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            new FileReplacer(NewTask(workspace), Silent).Apply(source, target);
            Assert.Equal("new", workspace.ReadFile("target/app.txt"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Apply_OccupiedFile_FailsWithRollbackManifest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Unix 无强制文件共享锁，无法稳定模拟占用
        }

        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("target/app.txt", "old");
        var path = workspace.Resolve("target", "app.txt");

        // FileShare.Read 模拟真实占用：可读（备份可行）但拒绝删除/改名 → 替换阶段失败
        using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var exception = Assert.Throws<UpdaterFailureException>(() =>
            new FileReplacer(NewTask(workspace, retryCount: 0), Silent).Apply(source, target));

        Assert.Equal(ExitCodes.ReplaceFailed, exception.ExitCode);
        Assert.NotNull(exception.RollbackManifest);
        Assert.Contains("app.txt", exception.RollbackManifest!.BackedUpFiles);
    }

    [Fact]
    public void Rollback_RestoresBackedUpAndRemovesCreated()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("source/app.txt", "new");
        workspace.WriteFile("source/fresh.txt", "added");
        workspace.WriteFile("target/app.txt", "old");
        workspace.WriteFile("target/keep.txt", "keep");

        var manifest = new FileReplacer(NewTask(workspace), Silent).Apply(source, target);
        var restored = RollbackManager.Restore(target, manifest, retryCount: 0, retryDelayMs: 0, Silent);

        Assert.True(restored);
        Assert.Equal("old", workspace.ReadFile("target/app.txt"));
        Assert.False(File.Exists(workspace.Resolve("target", "fresh.txt")));
        Assert.Equal("keep", workspace.ReadFile("target/keep.txt"));
    }

    private static UpdaterTaskFile NewTask(TestWorkspace workspace, string strategy = ReplaceStrategies.Merge,
        bool deleteOrphans = false, List<string>? exclude = null, List<string>? preserve = null, int retryCount = 0) =>
        new()
        {
            AppId = "test",
            Install = new InstallSection
            {
                Strategy = strategy,
                DeleteOrphans = deleteOrphans,
                Exclude = exclude ?? [],
                Preserve = preserve ?? [],
                BackupPath = workspace.Resolve("backup"),
                ReplaceRetryCount = retryCount
            }
        };
}
