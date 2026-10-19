using AtomUpdate.Packager.Cli;

namespace AtomUpdate.Tests.Packager;

/// <summary>CLI 解析规则（docs/05 §2/§16）：双写法、可重复、标志、未知参数、缺值。</summary>
public sealed class CliParserTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsHelp()
    {
        var cli = CliParser.Parse([]);

        Assert.True(cli.HelpRequested);
        Assert.Null(cli.Command);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_HelpFlag_ReturnsHelp(string flag)
    {
        var cli = CliParser.Parse([flag]);

        Assert.True(cli.HelpRequested);
    }

    [Fact]
    public void Parse_HelpAfterCommand_StillHelp()
    {
        var cli = CliParser.Parse(["pack", "--help"]);

        Assert.Equal("pack", cli.Command);
        Assert.True(cli.HelpRequested);
        Assert.Empty(cli.Errors);
    }

    [Fact]
    public void Parse_UnknownCommand_ErrorAndNullCommand()
    {
        var cli = CliParser.Parse(["frobnicate"]);

        Assert.Null(cli.Command);
        Assert.Contains(cli.Errors, error => error.Contains("未知命令"));
    }

    [Fact]
    public void Parse_SeparateValue_Works()
    {
        var cli = CliParser.Parse(["pack", "--version", "1.2.0"]);

        Assert.Empty(cli.Errors);
        Assert.Equal("1.2.0", cli.Value("version"));
    }

    [Fact]
    public void Parse_EqualsValue_Works()
    {
        var cli = CliParser.Parse(["pack", "--version=1.2.0"]);

        Assert.Empty(cli.Errors);
        Assert.Equal("1.2.0", cli.Value("version"));
    }

    [Fact]
    public void Parse_RepeatableOption_CollectsAll()
    {
        var cli = CliParser.Parse(["publish", "--arch", "x64", "--arch=arm64"]);

        Assert.Empty(cli.Errors);
        Assert.Equal(["x64", "arm64"], cli.Values("arch"));
    }

    [Fact]
    public void Parse_UnknownOption_Error()
    {
        var cli = CliParser.Parse(["pack", "--frobnicate", "x"]);

        Assert.Contains(cli.Errors, error => error.Contains("不支持选项"));
    }

    [Fact]
    public void Parse_UnknownOptionForCommand_ErrorEvenIfOtherCommandKnowsIt()
    {
        // api-url 仅 list/publish 认识，pack 不认识
        var cli = CliParser.Parse(["pack", "--api-url", "http://x"]);

        Assert.Contains(cli.Errors, error => error.Contains("不支持选项"));
    }

    [Fact]
    public void Parse_MissingValueAtEnd_Error()
    {
        var cli = CliParser.Parse(["pack", "--version"]);

        Assert.Contains(cli.Errors, error => error.Contains("缺少值"));
    }

    [Fact]
    public void Parse_MissingValueBeforeAnotherOption_Error()
    {
        var cli = CliParser.Parse(["pack", "--version", "--arch", "x64"]);

        Assert.Contains(cli.Errors, error => error.Contains("缺少值"));
    }

    [Fact]
    public void Parse_BarePositional_Error()
    {
        var cli = CliParser.Parse(["pack", "1.2.0"]);

        Assert.Contains(cli.Errors, error => error.Contains("位置参数"));
    }

    [Fact]
    public void Parse_BooleanFlag_RecordedWithoutValue()
    {
        var cli = CliParser.Parse(["publish", "--publish"]);

        Assert.Empty(cli.Errors);
        Assert.True(cli.Has("publish"));
    }

    [Fact]
    public void Parse_BooleanFlagWithValue_Error()
    {
        var cli = CliParser.Parse(["publish", "--publish=true"]);

        Assert.Contains(cli.Errors, error => error.Contains("布尔标志"));
    }

    [Fact]
    public void Parse_WorkDirAcceptedForEveryCommand()
    {
        var cli = CliParser.Parse(["upload", "--work-dir", "C:\\tmp", "--help"]);

        Assert.Equal("C:\\tmp", cli.Value("work-dir"));
        Assert.True(cli.HelpRequested);
    }

    [Fact]
    public void ValueOrEnv_OptionWinsOverEnvironment()
    {
        var cli = CliParser.Parse(["list", "--project", "7"]);

        Assert.Equal("7", cli.ValueOrEnv("project", "ATOMUPDATE_PACKAGER_PROJECT"));
    }
}
