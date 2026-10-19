using System.Diagnostics;
using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Enums;
using AtomUpdate.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 示例宿主（06 M3 出口条件）：Generic Host 形态的控制台宿主；
// 改造成服务型宿主需额外引用 Microsoft.Extensions.Hosting.WindowsServices 并 builder.Services.UseWindowsService()
// （无 UI 宿主下通知本就是 Console，无需改动业务代码）。
// 用法：dotnet run --project samples/AtomUpdate.Sample.Console -- --server http://localhost:5062 \
//        --project-key sample --app-id com.sample.console --version 1.0.0 [--install] [--poll]
var usage = "未知参数：仅支持 --server/--project-key/--app-id/--version/--trigger/--install/--poll/--skip-update-check";
var overrides = new Dictionary<string, string>();
var install = false;
var poll = false;
var skipUpdateCheck = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--install":
            install = true;
            break;
        case "--poll":
            poll = true;
            break;
        case "--skip-update-check":
            skipUpdateCheck = true;
            break;
        case "--server":
        case "--project-key":
        case "--app-id":
        case "--version":
        case "--trigger":
            overrides[args[i]] = i + 1 < args.Length ? args[++i] : throw new ArgumentException(usage);
            break;
        default:
            Console.Error.WriteLine(usage);
            return 64;
    }
}

// content root 显式指向输出目录：dotnet run --project 会把工作目录设在仓库根，
// 默认从工作目录找 appsettings.json 会落空（配置层静默不生效）
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddAtomUpdate(options =>
{
    if (overrides.TryGetValue("--server", out var server))
    {
        options.ServerUrl = server;
    }

    if (overrides.TryGetValue("--project-key", out var projectKey))
    {
        options.ProjectKey = projectKey;
    }

    if (overrides.TryGetValue("--app-id", out var appId))
    {
        options.AppId = appId;
    }

    if (overrides.TryGetValue("--version", out var version))
    {
        options.CurrentVersion = version;
    }

    // 代码覆盖层：轮询演示时打开（--poll），否则一次性流程不需要后台轮询
    options.Polling.Enabled = poll;
});

using var host = builder.Build();
await host.StartAsync();

var updater = host.Services.GetRequiredService<IAtomUpdater>();
updater.StateChanged += (_, e) => Console.WriteLine($"[state] {e.OldState} → {e.NewState}");
updater.DownloadProgressChanged += (_, e) => Console.WriteLine(
    $"[progress] {e.Progress.Percent?.ToString("F1") ?? "?"}% " +
    $"{e.Progress.BytesReceived}/{e.Progress.TotalBytes?.ToString() ?? "?"}B " +
    $"{e.Progress.SpeedBytesPerSecond / 1024:F0}KB/s");
updater.UpdateReady += (_, e) => Console.WriteLine($"[ready] {e.Update.LatestVersion} → {e.PackagePath}");

Console.WriteLine($"宿主启动：appId={host.Services.GetRequiredService<AtomUpdateOptions>().AppId}，" +
                  $"当前版本={host.Services.GetRequiredService<AtomUpdateOptions>().CurrentVersion}");

if (skipUpdateCheck)
{
    // 更新器安装完成后以此参数重启主程序：跳过启动检查，防止"检查→安装→重启→再检查"循环（04 §5.2）
    Console.WriteLine("检测到 --skip-update-check：跳过本次启动检查（防更新循环）");
    await host.StopAsync();
    return 0;
}

var trigger = overrides.TryGetValue("--trigger", out var triggerText)
    ? Enum.Parse<UpdateTrigger>(triggerText, ignoreCase: true)
    : UpdateTrigger.Manual;

var stopwatch = Stopwatch.StartNew();
var result = await updater.CheckAndDownloadAsync(trigger);

Console.WriteLine($"检查：Success={result.Check.Success} HasUpdate={result.Check.HasUpdate} " +
                  $"错误={result.Check.ErrorCode ?? "-"}");
Console.WriteLine(result.Check.HasUpdate
    ? $"下载：Success={result.Download.Success} 包={result.Download.PackagePath ?? "-"} " +
      $"错误={result.Download.ErrorCode ?? "-"} {result.Download.ErrorMessage}"
    : "下载：未触发（无可用更新或检查未成功）");
Console.WriteLine($"状态机落点：{updater.State}（耗时 {stopwatch.Elapsed.TotalSeconds:F1}s）");

if (install && updater.State == UpdateState.ReadyToInstall)
{
    var installed = await updater.InstallAndRestartAsync();
    Console.WriteLine($"安装调度：Success={installed.Success} Scheduled={installed.Scheduled} " +
                      $"错误={installed.ErrorCode ?? "-"}");
}

if (poll)
{
    Console.WriteLine("轮询中（Ctrl+C 结束）");
    await host.WaitForShutdownAsync();
}

await host.StopAsync();

return updater.State switch
{
    UpdateState.NoUpdate => 0,
    // 安装已调度、等待宿主退出触发（更新器归 M4）同样算成功
    UpdateState.ReadyToInstall or UpdateState.InstallScheduled or UpdateState.RestartPending => 0,
    UpdateState.Failed => 2,
    _ => 1
};
