using AtomUpdate.Updater.Cli;

namespace AtomUpdate.Tests.Updater;

public class CliArgumentsTests
{
    [Fact]
    public void Parse_SpaceSeparatedValues()
    {
        var (options, errors) = CliArguments.Parse(
            ["--package", "a.zip", "--target", @"C:\app", "--to-version", "1.0.1"]);

        Assert.Empty(errors);
        Assert.Equal("a.zip", options.Package);
        Assert.Equal(@"C:\app", options.Target);
        Assert.Equal("1.0.1", options.ToVersion);
    }

    [Fact]
    public void Parse_EqualsSeparatedValues()
    {
        var (options, errors) = CliArguments.Parse(
            ["--package=a.zip", "--target=/opt/app", "--from-version=1.0.0"]);

        Assert.Empty(errors);
        Assert.Equal("a.zip", options.Package);
        Assert.Equal("/opt/app", options.Target);
        Assert.Equal("1.0.0", options.FromVersion);
    }

    [Fact]
    public void Parse_FlagsAndPid()
    {
        var (options, errors) = CliArguments.Parse(
            ["--dry-run", "--no-restart", "--no-rollback", "--verbose", "--elevated", "--replace-pid", "1234"]);

        Assert.Empty(errors);
        Assert.True(options.DryRun);
        Assert.True(options.NoRestart);
        Assert.True(options.NoRollback);
        Assert.True(options.Verbose);
        Assert.True(options.Elevated);
        Assert.Equal(1234, options.ReplacePid);
    }

    [Fact]
    public void Parse_MissingValueReported()
    {
        var (_, errors) = CliArguments.Parse(["--package"]);

        Assert.Contains(errors, error => error.Contains("--package 缺少值"));
    }

    [Fact]
    public void Parse_UnknownArgumentReported()
    {
        var (_, errors) = CliArguments.Parse(["--bogus"]);

        Assert.Contains(errors, error => error.Contains("未知参数"));
    }

    [Fact]
    public void Parse_NonOptionArgumentReported()
    {
        var (_, errors) = CliArguments.Parse(["stray"]);

        Assert.Contains(errors, error => error.Contains("无法识别的参数"));
    }

    [Fact]
    public void Parse_InvalidReplacePidReported()
    {
        var (_, errors) = CliArguments.Parse(["--replace-pid", "abc"]);

        Assert.Contains(errors, error => error.Contains("--replace-pid 必须是整数"));
    }

    [Fact]
    public void Parse_HelpFlag()
    {
        var (options, errors) = CliArguments.Parse(["--help"]);

        Assert.Empty(errors);
        Assert.True(options.Help);
    }
}
