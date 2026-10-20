using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Core.Serialization;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Endpoints.Manage;

/// <summary>概览与审计：读接口，租户上下文由查询过滤器隔离。
/// 概览/统计汇总为重聚合查询，经 IReadCache 缓存 30s（键含租户与 appId 维度），写路径变更端点主动失效。</summary>
internal static class InsightEndpoints
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/overview", OverviewAsync).Produces<OverviewDto>();
        group.MapGet("/audit-logs", AuditLogsAsync).Produces<PagedResult<AuditLogDto>>();
        group.MapGet("/stats/installations", InstallationsAsync).Produces<PagedResult<InstallationDto>>();
        group.MapGet("/stats/installations/summary", InstallationSummaryAsync).Produces<InstallationSummaryDto>();
        group.MapGet("/stats/downloads/summary", DownloadSummaryAsync).Produces<DownloadSummaryDto>();
        group.MapGet("/stats/checks/summary", CheckSummaryAsync).Produces<CheckSummaryDto>();
        group.MapGet("/stats/checks", CheckLogsAsync).Produces<PagedResult<CheckLogDto>>();
    }

    private static async Task<IResult> OverviewAsync(AtomUpdateDbContext db, TenantContext tenantContext,
        IReadCache readCache, CancellationToken ct)
    {
        var overview = await readCache.GetAsync(
            $"t:{tenantContext.TenantId ?? 0}:stats:overview", LoadAsync, ct);
        return Results.Ok(overview);

        async Task<OverviewDto> LoadAsync(CancellationToken token)
        {
            var since24h = DateTimeOffset.UtcNow.AddHours(-24);
            var projectIds = db.Projects.Select(p => p.Id);
            var appIds = db.Apps.Select(a => a.Id);

            var checks = db.UpdateCheckLogs.AsNoTracking().Where(l => l.CheckedAt >= since24h);
            var totalChecks = await checks.CountAsync(token);
            var etagHits = totalChecks == 0 ? 0 : await checks.CountAsync(l => l.ETagHit, token);

            return new OverviewDto
            {
                Projects = await projectIds.CountAsync(token),
                Apps = await appIds.CountAsync(token),
                PublishedReleases = await db.Releases.CountAsync(r => r.Status == ReleaseStatus.Published, token),
                Checks24h = totalChecks,
                ActiveInstalls24h = await checks.Select(l => l.InstallationId).Distinct().CountAsync(token),
                ETagHitRate24h = totalChecks == 0 ? 0 : etagHits / (double)totalChecks,
                Users = await db.Users.CountAsync(token)
            };
        }
    }

    private static async Task<IResult> AuditLogsAsync(AtomUpdateDbContext db, int? page, int? pageSize,
        CancellationToken ct)
    {
        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.AuditLogs.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        // Actor 存 JWT sub（用户 Id）：批量解析显示名（DisplayName → UserName → Email 兜底），
        // 用户已删除或标识不可解析时回退原始值；IgnoreQueryFilters 按显式主键解析，避免跨租户场景解析失败
        var actorIds = items
            .Select(l => long.TryParse(l.Actor, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();
        var actors = actorIds.Count == 0
            ? []
            : await db.Users.AsNoTracking().IgnoreQueryFilters()
                .Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.UserName ?? u.Email, ct);

        return Results.Ok(new PagedResult<AuditLogDto>
        {
            Items = items.Select(l => new AuditLogDto
            {
                Id = l.Id,
                Actor = l.Actor,
                ActorName = long.TryParse(l.Actor, out var actorId) && actors.TryGetValue(actorId, out var name)
                    ? name
                    : l.Actor,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                Details = l.Details,
                CreatedAt = l.CreatedAt
            }).ToList(),
            Total = total
        });
    }

    // ─── 统计端点 ─────────────────────────────────────

    private static async Task<IResult> InstallationsAsync(AtomUpdateDbContext db, long? appId, string? keyword,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.Installations.AsNoTracking();
        if (appId is { } aid)
        {
            query = query.Where(i => i.AppId == aid);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // npgsql→ILike；SQLite 无 ILike 翻译，退化 Like（其 LIKE 对 ASCII 默认不区分大小写，行为对齐）
            query = db.Database.IsSqlite()
                ? query.Where(i => EF.Functions.Like(i.InstallationId, $"%{keyword}%"))
                : query.Where(i => EF.Functions.ILike(i.InstallationId, $"%{keyword}%"));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.LastSeenAt)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<InstallationDto>
        {
            Items = items.Select(i => new InstallationDto
            {
                Id = i.Id,
                InstallationId = i.InstallationId,
                MachineFingerprintHash = i.MachineFingerprintHash,
                Channel = i.Channel,
                LastVersion = i.LastVersion,
                FirstSeenAt = i.FirstSeenAt,
                LastSeenAt = i.LastSeenAt
            }).ToList(),
            Total = total
        });
    }

    private static async Task<IResult> InstallationSummaryAsync(AtomUpdateDbContext db, long? appId,
        TenantContext tenantContext, IReadCache readCache, CancellationToken ct)
    {
        var summary = await readCache.GetAsync(
            $"t:{tenantContext.TenantId ?? 0}:stats:inst:{appId ?? 0}", LoadAsync, ct);
        return Results.Ok(summary);

        async Task<InstallationSummaryDto> LoadAsync(CancellationToken token)
        {
            var now = DateTimeOffset.UtcNow;
            var query = db.Installations.AsNoTracking();
            if (appId is { } aid)
            {
                query = query.Where(i => i.AppId == aid);
            }

            var total = await query.CountAsync(token);
            var withoutFingerprint = await query.CountAsync(i => i.MachineFingerprintHash == null, token);

            return new InstallationSummaryDto
            {
                Total = total,
                // 机器指纹去重：distinct 非空指纹数；未上报指纹的实例单列
                DistinctFingerprints = await query.Where(i => i.MachineFingerprintHash != null)
                    .Select(i => i.MachineFingerprintHash!).Distinct().CountAsync(token),
                WithoutFingerprint = withoutFingerprint,
                Active24h = await query.CountAsync(i => i.LastSeenAt >= now.AddHours(-24), token),
                Active7d = await query.CountAsync(i => i.LastSeenAt >= now.AddDays(-7), token),
                ByVersion = await query.Where(i => i.LastVersion != null)
                    .GroupBy(i => i.LastVersion!)
                    .Select(g => new VersionCountDto { Version = g.Key, Count = g.Count() })
                    .OrderByDescending(v => v.Count).Take(10).ToListAsync(token),
                ByChannel = await query.Where(i => i.Channel != null)
                    .GroupBy(i => i.Channel!.Value)
                    .Select(g => new ChannelCountDto { Channel = g.Key, Count = g.Count() })
                    .OrderByDescending(c => c.Count).ToListAsync(token)
            };
        }
    }

    private static async Task<IResult> DownloadSummaryAsync(AtomUpdateDbContext db, long? appId,
        TenantContext tenantContext, IReadCache readCache, CancellationToken ct)
    {
        var summary = await readCache.GetAsync(
            $"t:{tenantContext.TenantId ?? 0}:stats:dl:{appId ?? 0}", LoadAsync, ct);
        return Results.Ok(summary);

        async Task<DownloadSummaryDto> LoadAsync(CancellationToken token)
        {
            var now = DateTimeOffset.UtcNow;
            var query = db.DownloadLogs.AsNoTracking();
            if (appId is { } aid)
            {
                query = query.Where(l => l.AppId == aid);
            }

            var since7d = query.Where(l => l.DownloadedAt >= now.AddDays(-7));
            var count7d = await since7d.CountAsync(token);
            var success7d = count7d == 0 ? 0 : await since7d.CountAsync(l => l.Success, token);
            var withKind = await since7d.CountAsync(l => l.Kind != null, token);
            var delta = withKind == 0 ? 0 : await since7d.CountAsync(l => l.Kind == AssetKind.Delta, token);

            return new DownloadSummaryDto
            {
                Downloads24h = await query.CountAsync(l => l.DownloadedAt >= now.AddHours(-24), token),
                Downloads7d = count7d,
                SuccessRate7d = count7d == 0 ? 0 : success7d / (double)count7d,
                Bytes7d = count7d == 0 ? 0 : await since7d.SumAsync(l => l.Bytes, token),
                AvgDurationMs7d = count7d == 0 ? 0 : await since7d.AverageAsync(l => (double)l.DurationMs, token),
                DeltaShare7d = withKind == 0 ? 0 : delta / (double)withKind
            };
        }
    }

    private static async Task<IResult> CheckSummaryAsync(AtomUpdateDbContext db, long? appId,
        TenantContext tenantContext, IReadCache readCache, CancellationToken ct)
    {
        var summary = await readCache.GetAsync(
            $"t:{tenantContext.TenantId ?? 0}:stats:chk:{appId ?? 0}", LoadAsync, ct);
        return Results.Ok(summary);

        async Task<CheckSummaryDto> LoadAsync(CancellationToken token)
        {
            var now = DateTimeOffset.UtcNow;
            var query = db.UpdateCheckLogs.AsNoTracking();
            if (appId is { } aid)
            {
                query = query.Where(l => l.AppId == aid);
            }

            var since7d = query.Where(l => l.CheckedAt >= now.AddDays(-7));
            var count7d = await since7d.CountAsync(token);
            var etagHits = count7d == 0 ? 0 : await since7d.CountAsync(l => l.ETagHit, token);
            var policyUnchanged = count7d == 0 ? 0 : await since7d.CountAsync(l => l.PolicyUnchanged, token);

            return new CheckSummaryDto
            {
                Checks24h = await query.CountAsync(l => l.CheckedAt >= now.AddHours(-24), token),
                Checks7d = count7d,
                ETagHitRate7d = count7d == 0 ? 0 : etagHits / (double)count7d,
                PolicyUnchangedRate7d = count7d == 0 ? 0 : policyUnchanged / (double)count7d
            };
        }
    }

    /// <summary>检测记录逐条列表（时间倒序）：可选项目/应用筛选与应用标识、版本、IP 模糊匹配；
    /// 应用删除后日志保留（快照设计），AppName 为空时前端退化为 AppKey 展示。</summary>
    private static async Task<IResult> CheckLogsAsync(AtomUpdateDbContext db, long? projectId, long? appId,
        string? keyword, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.UpdateCheckLogs.AsNoTracking();
        if (appId is { } aid)
        {
            query = query.Where(l => l.AppId == aid);
        }

        if (projectId is { } pid)
        {
            query = query.Where(l => db.Apps.Any(a => a.Id == l.AppId && a.ProjectId == pid));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // npgsql→ILike；SQLite 无 ILike 翻译，退化 Like（其 LIKE 对 ASCII 默认不区分大小写，行为对齐）
            query = db.Database.IsSqlite()
                ? query.Where(l => EF.Functions.Like(l.AppKey, $"%{keyword}%")
                                   || EF.Functions.Like(l.CurrentVersion, $"%{keyword}%")
                                   || (l.ClientIp != null && EF.Functions.Like(l.ClientIp, $"%{keyword}%")))
                : query.Where(l => EF.Functions.ILike(l.AppKey, $"%{keyword}%")
                                   || EF.Functions.ILike(l.CurrentVersion, $"%{keyword}%")
                                   || (l.ClientIp != null && EF.Functions.ILike(l.ClientIp, $"%{keyword}%")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(l => new
            {
                l.Id,
                l.AppId,
                l.AppKey,
                l.Platform,
                l.Arch,
                l.CurrentVersion,
                l.LatestVersion,
                l.HasUpdate,
                l.ETagHit,
                l.ClientIp,
                l.CheckedAt,
                AppName = db.Apps.Where(a => a.Id == l.AppId).Select(a => a.Name).FirstOrDefault()
            })
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<CheckLogDto>
        {
            Items = items.Select(l => new CheckLogDto
            {
                Id = l.Id,
                AppKey = l.AppKey,
                AppName = l.AppName,
                Platform = l.Platform,
                Arch = l.Arch,
                CurrentVersion = l.CurrentVersion,
                LatestVersion = l.LatestVersion,
                HasUpdate = l.HasUpdate,
                ETagHit = l.ETagHit,
                ClientIp = l.ClientIp,
                CheckedAt = l.CheckedAt
            }).ToList(),
            Total = total
        });
    }
}
