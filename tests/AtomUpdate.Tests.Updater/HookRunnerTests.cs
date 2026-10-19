using System.Diagnostics;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Hooks;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Tests.Updater;

public class HookRunnerTests
{
    private static readonly UpdaterLog Silent = UpdaterLog.ConsoleOnly(UpdaterLogLevel.None);

    [Fact]
    public void ResolveCommand_MapsInterpretersByExtension()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        workspace.WriteFile("target/script.bat", "@echo off");
        workspace.WriteFile("target/script.cmd", "@echo off");
        workspace.WriteFile("target/script.ps1", "# noop");
        workspace.WriteFile("target/script.sh", "#!/bin/sh");

        Assert.Equal(("cmd", $"/c \"{workspace.Resolve("target", "script.bat")}\""),
            HookRunner.ResolveCommand("script.bat", target));
        Assert.Equal(("cmd", $"/c \"{workspace.Resolve("target", "script.cmd")}\""),
            HookRunner.ResolveCommand("script.cmd", target));
        Assert.Equal(("powershell", $"-ExecutionPolicy Bypass -File \"{workspace.Resolve("target", "script.ps1")}\""),
            HookRunner.ResolveCommand("script.ps1", target));
        Assert.Equal(("/bin/sh", $"\"{workspace.Resolve("target", "script.sh")}\""),
            HookRunner.ResolveCommand("script.sh", target));
    }

    [Fact]
    public void ResolveCommand_ExeAndUnresolvedRelativePassthrough()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");

        Assert.Equal(("/usr/bin/tool", ""), HookRunner.ResolveCommand("/usr/bin/tool", target));

        // 相对路径未命中目标目录：保持原样，仍按扩展名映射解释器（工作目录即目标目录）
        Assert.Equal(("cmd", "/c \"not-there.bat\""), HookRunner.ResolveCommand("not-there.bat", target));
    }

    [Fact]
    public async Task RunAsync_ExecutesHookWithEnvironment()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var command = CreateScript(workspace, "hook", $"echo %ATOMUPDATE_TO_VERSION% > out.txt", "echo $ATOMUPDATE_TO_VERSION > out.txt");

        var task = new UpdaterTaskFile
        {
            ToVersion = "9.9.9",
            Install = new InstallSection
            {
                Hooks = { [HookStages.PostInstall] = new HookCommandList([command]) },
                HookTimeoutSeconds = 30
            }
        };

        var ok = await new HookRunner(task, Silent).RunAsync(HookStages.PostInstall, target);

        Assert.True(ok);
        Assert.Equal("9.9.9", workspace.ReadFile("target/out.txt").Trim());
    }

    [Fact]
    public async Task RunAsync_NoHooksForStage_ReturnsTrue()
    {
        using var workspace = new TestWorkspace();

        var ok = await new HookRunner(new UpdaterTaskFile(), Silent).RunAsync(HookStages.PreRestart, workspace.Root);

        Assert.True(ok);
    }

    [Fact]
    public async Task RunAsync_AbortPolicy_FailsOnNonZeroExit_ContinuePolicyPasses()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var command = CreateScript(workspace, "fail", "exit /b 1", "exit 1");

        var abortTask = new UpdaterTaskFile
        {
            Install = new InstallSection
            {
                Hooks = { [HookStages.PreInstall] = new HookCommandList([command]) },
                HookFailPolicy = HookFailPolicies.Abort,
                HookTimeoutSeconds = 30
            }
        };
        var continueTask = new UpdaterTaskFile
        {
            Install = new InstallSection
            {
                Hooks = { [HookStages.PreInstall] = new HookCommandList([command]) },
                HookFailPolicy = HookFailPolicies.Continue,
                HookTimeoutSeconds = 30
            }
        };

        Assert.False(await new HookRunner(abortTask, Silent).RunAsync(HookStages.PreInstall, target));
        Assert.True(await new HookRunner(continueTask, Silent).RunAsync(HookStages.PreInstall, target));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsAndFails()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.CreateDirectory("target");
        var command = CreateScript(workspace, "slow", "ping -n 6 127.0.0.1 >nul", "sleep 5");

        var task = new UpdaterTaskFile
        {
            Install = new InstallSection
            {
                Hooks = { [HookStages.PreInstall] = new HookCommandList([command]) },
                HookFailPolicy = HookFailPolicies.Abort,
                HookTimeoutSeconds = 1
            }
        };

        var stopwatch = Stopwatch.StartNew();
        var ok = await new HookRunner(task, Silent).RunAsync(HookStages.PreInstall, target);
        stopwatch.Stop();

        Assert.False(ok);
        Assert.True(stopwatch.ElapsedMilliseconds < 10_000, $"耗时 {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>按平台创建可执行脚本：Windows .bat / Unix .sh（+x）。</summary>
    private static string CreateScript(TestWorkspace workspace, string name, string windowsBody, string unixBody)
    {
        if (OperatingSystem.IsWindows())
        {
            workspace.WriteFile($"target/{name}.bat", $"@echo off\r\n{windowsBody}\r\n");
            return $"{name}.bat";
        }

        workspace.WriteFile($"target/{name}.sh", $"#!/bin/sh\n{unixBody}\n");
        File.SetUnixFileMode(workspace.Resolve("target", $"{name}.sh"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return $"{name}.sh";
    }
}
