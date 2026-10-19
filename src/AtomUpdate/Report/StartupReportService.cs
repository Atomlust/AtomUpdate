using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Configuration;
using AtomUpdate.Logging;
using Microsoft.Extensions.Hosting;

namespace AtomUpdate.Report;

/// <summary>启动上报（04 §3.14：宿主启动时读取 pending-reports 补报）。与轮询解耦——关闭轮询也要补报，否则离线队列只增不减。</summary>
public sealed class StartupReportService(
    IAtomUpdater updater,
    AtomUpdateOptions options,
    IUpdateLogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Report.Enabled || !options.Report.ReportOnStartup)
        {
            return;
        }

        try
        {
            var sent = await updater.ReportStartupAsync(stoppingToken);
            logger.Information(sent ? "启动上报与补报已发送" : "启动上报未送达，事件已入补报队列");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Warning("启动上报失败", exception);
        }
    }
}
