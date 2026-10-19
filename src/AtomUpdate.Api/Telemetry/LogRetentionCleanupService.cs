using System.Linq.Expressions;
using AtomUpdate.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Telemetry;

/// <summary>运行日志保留清理：每 6 小时两步——①按保留期（系统设置 logging.retention_days，默认 7 天）分批删除
/// 三类运行日志（检查/下载/安装上报）；②检测记录每应用只保留最近 N 条（logging.check_log_per_app_limit，默认 500）。
/// 每批 ≤5000 条避免长事务锁表；失败仅告警不终止宿主。</summary>
public sealed class LogRetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<LogRetentionCleanupService> logger) : BackgroundService
{
    private const int NpgsqlBatchSize = 5000;

    /// <summary>SQLite 单写者：大批删除易与批量落库争锁，批次降为 1000（docs/11 §3.4）。</summary>
    private const int SqliteBatchSize = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await CleanupAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // 停机退出
        }
    }

    /// <summary>执行一轮清理（启动后立即跑一次，此后每 6 小时）。参数读系统设置（DB 优先，代码默认兜底）：
    /// 保留期 ≤0 关闭时间清理；每应用上限 ≤0 关闭数量裁剪。</summary>
    public async Task CleanupAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>();
            var settings = scope.ServiceProvider.GetRequiredService<Settings.SettingsService>();
            var (retentionDays, perAppLimit) = await settings.GetLoggingAsync(ct);

            var checkLogs = 0;
            var downloadLogs = 0;
            var reports = 0;
            if (retentionDays > 0)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

                // 后台线程无租户上下文，三类表均为全局快照设计，统一 IgnoreQueryFilters 跨租户清理
                checkLogs = await DeleteBatchesAsync(db,
                    db.UpdateCheckLogs.IgnoreQueryFilters().Where(log => log.CheckedAt < cutoff), log => log.Id, ct);
                downloadLogs = await DeleteBatchesAsync(db,
                    db.DownloadLogs.IgnoreQueryFilters().Where(log => log.DownloadedAt < cutoff), log => log.Id, ct);
                reports = await DeleteBatchesAsync(db,
                    db.InstallationReports.IgnoreQueryFilters().Where(report => report.ReportedAt < cutoff), report => report.Id, ct);
            }

            var trimmed = await TrimCheckLogsByAppAsync(db, perAppLimit, ct);
            logger.LogInformation(
                "运行日志清理完成（保留 {RetentionDays} 天）：检查 {CheckLogs} / 下载 {DownloadLogs} / 安装 {Reports} 条，检测记录每应用 {Limit} 条上限裁剪 {Trimmed} 条",
                retentionDays, checkLogs, downloadLogs, reports, perAppLimit, trimmed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "运行日志清理失败");
        }
    }

    /// <summary>检测记录每应用只保留最近 {limit} 条：分组找出超限应用（私有化场景应用数有限），
    /// 以第 N 条的 Id 为阈值批量删除更旧记录；≤0 视为关闭。雪花 Id 单调递增与时间同序，
    /// 按 Id 比较避免同秒时间戳边界抖动。</summary>
    internal async Task<int> TrimCheckLogsByAppAsync(AtomUpdateDbContext db, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return 0;
        }

        var overLimitApps = await db.UpdateCheckLogs.IgnoreQueryFilters()
            .GroupBy(l => l.AppId)
            .Where(g => g.Count() > limit)
            .Select(g => g.Key)
            .ToListAsync(ct);
        if (overLimitApps.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var appId in overLimitApps)
        {
            var thresholdId = await db.UpdateCheckLogs.IgnoreQueryFilters()
                .Where(l => l.AppId == appId)
                .OrderByDescending(l => l.Id)
                .Skip(limit - 1)
                .Select(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (thresholdId == 0)
            {
                continue;
            }

            total += await DeleteBatchesAsync(db,
                db.UpdateCheckLogs.IgnoreQueryFilters().Where(l => l.AppId == appId && l.Id < thresholdId), l => l.Id, ct);
        }

        return total;
    }

    /// <summary>分批删除，直到删尽或不足一批。显式排序 + Take + ExecuteDelete 由 EF 统一翻译为
    /// DELETE WHERE Id IN (SELECT ... ORDER BY ... LIMIT n) 单语句（npgsql 为 ctid 形式，SQLite
    /// 为主键形式），不依赖 DELETE ... LIMIT 语法；排序键由调用方给出（均为单调递增的雪花 Id）。
    /// 不可省排序：无序 Take 会触发 EF Core 行限制无排序告警，且启动即跑的首轮清理会让三类
    /// 日志各刷一条（容器启动日志实见）。SQLite 批 1000，防长事务锁库（docs/11 §3.4）。</summary>
    private static async Task<int> DeleteBatchesAsync<TEntity>(AtomUpdateDbContext db, IQueryable<TEntity> expired,
        Expression<Func<TEntity, long>> orderBy, CancellationToken ct) where TEntity : class
    {
        var batchSize = db.Database.IsSqlite() ? SqliteBatchSize : NpgsqlBatchSize;
        var total = 0;
        int deleted;
        do
        {
            deleted = await expired.OrderBy(orderBy).Take(batchSize).ExecuteDeleteAsync(ct);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }
}
