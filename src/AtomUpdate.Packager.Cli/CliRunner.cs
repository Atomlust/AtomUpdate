using AtomUpdate.Packager.Cli.Commands;
using AtomUpdate.Packager.Core;

namespace AtomUpdate.Packager.Cli;

/// <summary>CLI 编排入口：解析参数并执行子命令（publish/pack/upload/list），GUI 复用同一 Runner。
/// 逻辑放 public 类型（docs/05 §16），Program.Main 仅是薄壳。退出码见 PackagerExitCodes。</summary>
public static class CliRunner
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ParsedCli cli;
        try
        {
            cli = CliParser.Parse(args);
        }
        catch (Exception ex)
        {
            error.WriteLine($"参数解析失败：{ex.Message}");
            return PackagerExitCodes.Arguments;
        }

        if (cli.HelpRequested)
        {
            output.WriteLine(CliParser.Usage);
            return PackagerExitCodes.Success;
        }

        if (cli.Errors.Count > 0)
        {
            foreach (var message in cli.Errors)
            {
                error.WriteLine(message);
            }

            error.WriteLine();
            error.WriteLine("使用 -h/--help 查看用法");
            return cli.Command is null ? PackagerExitCodes.Unknown : PackagerExitCodes.Arguments;
        }

        try
        {
            var run = cli.Command switch
            {
                "pack" => PackCommand.ExecuteAsync(cli, output, CancellationToken.None),
                "upload" => UploadCommand.ExecuteAsync(cli, output, CancellationToken.None),
                "list" => ListCommand.ExecuteAsync(cli, output, CancellationToken.None),
                "publish" => PublishCommand.ExecuteAsync(cli, output, CancellationToken.None),
                _ => throw new InvalidOperationException($"未分发命令：{cli.Command}")
            };
            return run.GetAwaiter().GetResult();
        }
        catch (CliPublishStageException ex)
        {
            error.WriteLine(ex.Message);
            return PackagerExitCodes.Publish;
        }
        catch (CliArgumentException ex)
        {
            error.WriteLine($"参数错误：{ex.Message}");
            return PackagerExitCodes.Arguments;
        }
        catch (PackagerException ex)
        {
            error.WriteLine($"失败 [{ex.Code}] {ex.Message}");
            return MapCode(ex.Code);
        }
        catch (Exception ex)
        {
            error.WriteLine($"未预期错误：{ex.GetType().Name}: {ex.Message}");
            return PackagerExitCodes.Unknown;
        }
    }

    /// <summary>错误码 → 退出码（docs/05 §14/§16）。</summary>
    private static int MapCode(string code) => code switch
    {
        PackagerErrorCodes.SourceNotFound or
            PackagerErrorCodes.SourceEmpty or
            PackagerErrorCodes.UnsupportedPackageType or
            PackagerErrorCodes.IsccNotFound or
            PackagerErrorCodes.IsccFailed or
            PackagerErrorCodes.DeltaBaselineInvalid => PackagerExitCodes.Packaging,

        PackagerErrorCodes.UploadFailed => PackagerExitCodes.Upload,

        PackagerErrorCodes.ApiFailed or
            PackagerErrorCodes.ReleaseExists => PackagerExitCodes.Push,

        _ => PackagerExitCodes.Unknown
    };
}
