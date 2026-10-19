using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Telemetry;

/// <summary>后台批量落库下载上报：每批 ≤200 条；写失败整批丢弃告警，不向读路径回传错误。
/// （InstallationReport 幂等语义要求同步插入，不经此队列。）</summary>
public sealed class ReportWriterService(
    ReportQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ReportWriterService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = queue.Reader;
        var batch = new List<ReportQueueItem>(BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < BatchSize && reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count > 0)
                {
                    await FlushAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停机：走 finally 清空队列剩余
        }
        finally
        {
            while (true)
            {
                while (batch.Count < BatchSize && reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count == 0)
                {
                    break;
                }

                await FlushAsync(batch, CancellationToken.None);
                batch.Clear();
            }
        }
    }

    private async Task FlushAsync(List<ReportQueueItem> batch, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>();
            db.DownloadLogs.AddRange(batch.Select(x => x.Download));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "写入 {Count} 条下载上报失败，整批丢弃", batch.Count);
        }
    }
}
