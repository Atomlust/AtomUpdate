using AtomUpdate.Packager.Cli;

namespace AtomUpdate.Tests.Packager;

/// <summary>CliRunner 退出码映射 + pack 端到端（docs/05 §16）：0/1 参数/3 打包/5 上传。</summary>
public sealed class CliRunnerTests
{
    private static (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CliRunner.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static string[] PackArgs(TestWorkspace ws, params string[] extra) =>
        [.. new[]
        {
            "pack", "--project-key", "myproj", "--app-id", "com.example.app",
            "--version", "1.2.0", "--source", ws.SourceDirectory,
            "--platform", "windows", "--arch", "x64", "--output", ws.OutputDirectory
        }, .. extra];

    [Fact]
    public void Help_ReturnsSuccessAndPrintsUsage()
    {
        var (code, output, _) = Run("--help");

        Assert.Equal(PackagerExitCodes.Success, code);
        Assert.Contains("publish", output);
        Assert.Contains("pack", output);
    }

    [Fact]
    public void UnknownCommand_ReturnsUnknownExitCode()
    {
        var (code, _, error) = Run("frobnicate");

        Assert.Equal(PackagerExitCodes.Unknown, code);
        Assert.Contains("未知命令", error);
    }

    [Fact]
    public void UnknownOption_ReturnsArgumentsExitCode()
    {
        var (code, _, _) = Run("pack", "--nope", "x");

        Assert.Equal(PackagerExitCodes.Arguments, code);
    }

    [Fact]
    public void Pack_MissingRequired_ReturnsArguments()
    {
        var (code, _, error) = Run("pack");

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("project-key", error);
    }

    [Fact]
    public void Pack_BadSemVer_ReturnsArguments()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "x");
        var args = new[]
        {
            "pack", "--project-key", "k", "--app-id", "a",
            "--version", "not-a-version", "--source", ws.SourceDirectory,
            "--platform", "windows", "--arch", "x64"
        };
        var (code, _, error) = Run(args);

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("语义版本", error);
    }

    [Fact]
    public void Pack_SourceMissing_ReturnsPackagingExitCode()
    {
        using var ws = new TestWorkspace();
        var missing = Path.Combine(ws.Root, "nope");

        var (code, _, _) = Run("pack", "--project-key", "k", "--app-id", "a",
            "--version", "1.0.0", "--source", missing, "--platform", "windows", "--arch", "x64");

        Assert.Equal(PackagerExitCodes.Packaging, code);
    }

    [Fact]
    public void Pack_UnsupportedType_ReturnsPackagingExitCode()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("app.txt", "x");

        var (code, _, _) = Run(PackArgs(ws, "--type", "msi"));

        Assert.Equal(PackagerExitCodes.Packaging, code);
    }

    [Fact]
    public void Pack_BaselineWithoutFromVersion_ReturnsArguments()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("app.txt", "x");

        var (code, _, error) = Run(PackArgs(ws, "--baseline", ws.BaselineDirectory));

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("baseline", error);
    }

    [Fact]
    public void Pack_HappyPath_CreatesZipAndReportsFullLine()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "hello");

        var (code, output, errorText) = Run(PackArgs(ws));

        Assert.True(code == PackagerExitCodes.Success, errorText);
        Assert.Contains("FULL", output);
        Assert.Contains("windows-x64", output);
        Assert.Single(Directory.EnumerateFiles(ws.OutputDirectory, "*.zip"));
    }

    [Fact]
    public void Pack_MultiArch_CreatesZipPerArch()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "hello");

        var (code, _, _) = Run(PackArgs(ws, "--arch", "x64", "--arch", "arm64"));

        Assert.Equal(PackagerExitCodes.Success, code);
        Assert.Equal(2, Directory.EnumerateFiles(ws.OutputDirectory, "*.zip").Count());
    }

    [Fact]
    public void Pack_WithBaseline_CreatesFullAndDelta()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("baseline/old.txt", "old");
        ws.WriteFile("source/app.txt", "new");

        var (code, output, _) = Run(PackArgs(ws,
            "--baseline", ws.BaselineDirectory, "--from-version", "1.1.0"));

        Assert.Equal(PackagerExitCodes.Success, code);
        Assert.Contains("FULL", output);
        Assert.Contains("DELTA", output);
        // 1 个 full zip + 1 个 delta zip
        Assert.Equal(2, Directory.EnumerateFiles(ws.OutputDirectory, "*.zip").Count());
    }

    [Fact]
    public void Pack_ProjectKeyAndAppId_FallBackToEnvironment()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "x");
        var previousKey = Environment.GetEnvironmentVariable("ATOMUPDATE_PACKAGER_PROJECT_KEY");
        var previousApp = Environment.GetEnvironmentVariable("ATOMUPDATE_PACKAGER_APP_ID");
        Environment.SetEnvironmentVariable("ATOMUPDATE_PACKAGER_PROJECT_KEY", "envproj");
        Environment.SetEnvironmentVariable("ATOMUPDATE_PACKAGER_APP_ID", "envapp");
        try
        {
            var (code, _, _) = Run("pack", "--version", "1.0.0",
                "--source", ws.SourceDirectory, "--platform", "windows", "--arch", "x64",
                "--output", ws.OutputDirectory);

            Assert.Equal(PackagerExitCodes.Success, code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATOMUPDATE_PACKAGER_PROJECT_KEY", previousKey);
            Environment.SetEnvironmentVariable("ATOMUPDATE_PACKAGER_APP_ID", previousApp);
        }
    }

    [Fact]
    public void Upload_MissingRequired_ReturnsArguments()
    {
        using var ws = new TestWorkspace();

        var (code, _, error) = Run("upload", "--work-dir", ws.Root);

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("api-url", error);
    }

    [Fact]
    public void Upload_MissingFileOption_ReturnsArguments()
    {
        using var ws = new TestWorkspace();

        var (code, _, error) = Run("upload", "--work-dir", ws.Root,
            "--api-url", "http://127.0.0.1:1", "--email", "a@b.c", "--password", "p",
            "--project", "1", "--app", "1", "--version", "1.0.0");

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("--file", error);
    }

    [Fact]
    public void Upload_FileMissing_ReturnsUploadExitCode()
    {
        using var ws = new TestWorkspace();
        var missing = Path.Combine(ws.Root, "ghost.bin");

        var (code, _, error) = Run("upload", "--work-dir", ws.Root,
            "--api-url", "http://127.0.0.1:1", "--email", "a@b.c", "--password", "p",
            "--project", "1", "--app", "1", "--version", "1.0.0", "--file", missing);

        Assert.Equal(PackagerExitCodes.Upload, code);
        Assert.Contains("文件不存在", error);
    }

    [Fact]
    public void List_BadProjectId_ReturnsArguments()
    {
        var (code, _, _) = Run("list", "--project", "abc", "--app", "1",
            "--api-url", "http://x", "--email", "a@b.c", "--password", "p");

        Assert.Equal(PackagerExitCodes.Arguments, code);
    }

    private static string[] PublishRequired(TestWorkspace ws) =>
    [
        "publish", "--work-dir", ws.Root,
        "--project-key", "myproj", "--app-id", "com.example.app",
        "--version", "1.2.0", "--source", ws.SourceDirectory,
        "--platform", "windows", "--arch", "x64",
        "--project", "42", "--app", "7",
        "--api-url", "http://127.0.0.1:1",
        "--email", "admin@example.com", "--password", "secret"
    ];

    [Fact]
    public void Publish_WithoutAnyArgs_ReturnsArguments()
    {
        var (code, _, _) = Run("publish");

        Assert.Equal(PackagerExitCodes.Arguments, code);
    }

    [Fact]
    public void Publish_PublishFlagWithoutChannels_ReturnsArguments()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "x");

        var (code, _, error) = Run([.. PublishRequired(ws), "--publish"]);

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("channels", error);
    }

    [Fact]
    public void Publish_ChannelsWithoutPublishFlag_ReturnsArguments()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "x");

        var (code, _, error) = Run([.. PublishRequired(ws), "--channels", "stable"]);

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("--publish", error);
    }

    [Fact]
    public void Publish_InvalidCreateStatus_ReturnsArguments()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "x");

        var (code, _, _) = Run([.. PublishRequired(ws), "--status", "published"]);

        Assert.Equal(PackagerExitCodes.Arguments, code);
    }

    [Fact]
    public void Publish_MissingEmail_ReturnsArguments()
    {
        using var ws = new TestWorkspace();
        ws.WriteFile("source/app.txt", "x");

        var args = PublishRequired(ws)
            .TakeWhile(arg => arg != "--email")
            .ToArray();

        var (code, _, error) = Run(args);

        Assert.Equal(PackagerExitCodes.Arguments, code);
        Assert.Contains("email", error);
    }
}
