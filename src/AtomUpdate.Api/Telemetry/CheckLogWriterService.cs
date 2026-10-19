using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;

namespace AtomUpdate.Api.Telemetry;

/// <summary>后台批量落库检查日志：每批 ≤200 条；写失败整批丢弃告警，不向读路径回传错误。</summary>
public sealed class CheckLogWriterService(
    CheckLogQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<CheckLogWriterService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = queue.Reader;
        var batch = new List<UpdateCheckLog>(BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < BatchSize && reader.TryRead(out var log))
                {
                    batch.Add(log);
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
            if (batch.Count > 0)
            {
                await FlushAsync(batch, CancellationToken.None);
                batch.Clear();
            }

            while (true)
            {
                while (batch.Count < BatchSize && reader.TryRead(out var log))
                {
                    batch.Add(log);
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

    private async Task FlushAsync(List<UpdateCheckLog> batch, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>();
            db.UpdateCheckLogs.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "写入 {Count} 条检查日志失败，整批丢弃", batch.Count);
        }
    }
}
