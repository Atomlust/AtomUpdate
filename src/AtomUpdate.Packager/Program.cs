using Avalonia;
using AtomUpdate.Packager.Cli;

namespace AtomUpdate.Packager;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // 双形态分发（docs/05 §16）：首 token 是已知子命令 → 纯 CLI；否则启动 Avalonia 向导。
        if (args.Length > 0 && CliCatalog.Commands.Contains(args[0]))
        {
            return CliRunner.Run(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return PackagerExitCodes.Success;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
