using System.Text.Json;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Telemetry;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Endpoints.Reports;

/// <summary>公开上报端点：install/download/report/batch 无需鉴权；租户按请求体 projectKey+appId 反查（与 check 同机制）；
/// check 事件 batch 不双计；校验失败宁丢不阻（不泄露应用存在性）。</summary>
internal static class ReportEndpoints
{
    private static readonly JsonSerializerOptions Json = AtomUpdateJson.Create();

    internal static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // 公开端点：租户隔离由请求体 projectKey 反查 + Global Query Filter 保证
        var group = app.MapGroup("")
            .WithTags("reports");

        group.MapPost(ApiRoutes.StatsInstall, StatsInstallAsync).Produces(StatusCodes.Status202Accepted);
        group.MapPost(ApiRoutes.StatsDownload, StatsDownloadAsync).Produces(StatusCodes.Status202Accepted);
        group.MapPost(ApiRoutes.ReportInstall, ReportInstallAsync).Produces<ReportInstallResponse>();
        group.MapPost(ApiRoutes.StatsBatch, StatsBatchAsync).Produces<StatsBatchResponse>();
    }

    // ─── /api/stats/install ──────────────────────────────
    private static async Task<IResult> StatsInstallAsync(
        StatsInstallRequest request,
        AtomUpdateDbContext db,
        TenantContext tenantContext,
        IReadCache readCache,
        CancellationToken ct)
    {
        var app = await ResolveAppAsync(db, tenantContext, readCache, request.ProjectKey, request.AppId, ct);
        if (app is null)
        {
            return Results.Accepted();
        }

        await UpsertInstallationAsync(db, app, request.InstallationId, request.Channel, null, request.Version, ct);
        return Results.Accepted();
    }

    // ─── /api/stats/download ─────────────────────────────
    private static async Task<IResult> StatsDownloadAsync(
        DownloadReport request,
        AtomUpdateDbContext db,
        TenantContext tenantContext,
        IReadCache readCache,
        ReportQueue queue,
        CancellationToken ct)
    {
        var app = await ResolveAppAsync(db, tenantContext, readCache, request.ProjectKey, request.AppId, ct);
        if (app is null)
        {
            return Results.Accepted();
        }

        EnqueueDownload(queue, app, request);
        await UpsertInstallationAsync(db, app, request.InstallationId, null, null, request.ToVersion, ct);
        return Results.Accepted();
    }

    // ─── /api/report/install ─────────────────────────────
    private static async Task<IResult> ReportInstallAsync(
        InstallReport request,
        AtomUpdateDbContext db,
        TenantContext tenantContext,
        IReadCache readCache,
        CancellationToken ct)
    {
        var app = await ResolveAppAsync(db, tenantContext, readCache, request.ProjectKey, request.AppId, ct);
        if (app is null)
        {
            return Results.Ok(new ReportInstallResponse { Accepted = false });
        }

        var duplicate = await TryInsertInstallReportAsync(db, ToEntity(app, request), ct);
        await UpsertInstallationAsync(db, app, request.InstallationId, null,
            request.MachineFingerprint, request.ToVersion, ct);
        return Results.Ok(new ReportInstallResponse { Accepted = true, Duplicate = duplicate });
    }

    // ─── /api/stats/batch ────────────────────────────────
    private static async Task<IResult> StatsBatchAsync(
        List<JsonElement> body,
        AtomUpdateDbContext db,
        TenantContext tenantContext,
        IReadCache readCache,
        ReportQueue queue,
        CancellationToken ct)
    {
        int accepted = 0, skipped = 0;
        foreach (var envelope in body)
        {
            if (!envelope.TryGetProperty("type", out var typeProp) ||
                !envelope.TryGetProperty("event", out var eventProp))
            {
                skipped++;
                continue;
            }

            try
            {
                switch (typeProp.GetString())
                {
                    // check 已服务端落 UpdateCheckLog，batch 不双计
                    case "check":
                        skipped++;
                        break;

                    case "download":
                        {
                            var ev = eventProp.Deserialize<DownloadReport>(Json);
                            var app = ev is null
                                ? null
                                : await ResolveAppAsync(db, tenantContext, readCache, ev.ProjectKey, ev.AppId, ct);
                            if (ev is null || app is null) { skipped++; break; }
                            if (!EnqueueDownload(queue, app, ev)) { skipped++; break; }
                            await UpsertInstallationAsync(db, app, ev.InstallationId, null, null, ev.ToVersion, ct);
                            accepted++;
                        }
                        break;

                    case "install":
                        {
                            var ev = eventProp.Deserialize<InstallReport>(Json);
                            var app = ev is null
                                ? null
                                : await ResolveAppAsync(db, tenantContext, readCache, ev.ProjectKey, ev.AppId, ct);
                            if (ev is null || app is null) { skipped++; break; }
                            await TryInsertInstallReportAsync(db, ToEntity(app, ev), ct);
                            await UpsertInstallationAsync(db, app, ev.InstallationId, null,
                                ev.MachineFingerprint, ev.ToVersion, ct);
                            accepted++;
                        }
                        break;

                    case "startup":
                        {
                            var ev = eventProp.Deserialize<StartupReport>(Json);
                            var app = ev is null
                                ? null
                                : await ResolveAppAsync(db, tenantContext, readCache, ev.ProjectKey, ev.AppId, ct);
                            if (ev is null || app is null) { skipped++; break; }
                            await UpsertInstallationAsync(db, app, ev.InstallationId, null,
                                ev.MachineFingerprint, ev.Version, ct);
                            accepted++;
                        }
                        break;

                    default:
                        skipped++;
                        break;
                }
            }
            catch (JsonException)
            {
                skipped++;
            }
        }

        return Results.Ok(new StatsBatchResponse { Accepted = accepted, Skipped = skipped });
    }

    // ─── 共享辅助 ────────────────────────────────────────

    /// <summary>匿名反查应用（与 check 同机制，解析走 IReadCache）：跨租户按 projectKey 唯一命中确定租户
    /// （歧义视为不存在，宁丢不阻），租户写入 TenantContext 使后续 Installation/InstallationReport 等
    /// 租户过滤查询生效。不存在返回 null（不泄露）。解析缓存在项目/应用变更端点失效。</summary>
    private static async Task<App?> ResolveAppAsync(AtomUpdateDbContext db, TenantContext tenantContext,
        IReadCache readCache, string projectKey, string appId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectKey) || string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        var lookup = await readCache.GetAsync($"anon:proj:{projectKey}", async token =>
        {
            var matches = await db.Projects.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Key == projectKey && p.Status == EntityStatus.Active)
                .OrderBy(p => p.Id)
                .Take(2)
                .ToListAsync(token);
            return new ProjectLookup(
                matches.Count == 1 ? matches[0] : null,
                Ambiguous: matches.Count > 1);
        }, ct);
        if (lookup.Project is not { } project)
        {
            return null;
        }

        tenantContext.TenantId = project.TenantId;
        return await readCache.GetAsync($"t:{project.TenantId}:app:{project.Id}:{appId}", async token =>
            await db.Apps.AsNoTracking().FirstOrDefaultAsync(
                a => a.ProjectId == project.Id && a.AppId == appId && a.Status == EntityStatus.Active, token), ct);
    }

    /// <summary>Installation upsert（按 AppId+InstallationId），自保存；并发唯一冲突时重载更新。QPS 低，同步小事务。</summary>
    private static async Task UpsertInstallationAsync(
        AtomUpdateDbContext db,
        App app,
        string installationId,
        UpdateChannel? channel,
        string? fingerprint,
        string? lastVersion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(installationId) || installationId.Length > 64)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var inst = await db.Installations
            .FirstOrDefaultAsync(i => i.AppId == app.Id && i.InstallationId == installationId, ct);

        if (inst is null)
        {
            db.Installations.Add(new Installation
            {
                TenantId = app.TenantId,
                AppId = app.Id,
                InstallationId = installationId,
                Channel = channel,
                MachineFingerprintHash = string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint,
                LastVersion = string.IsNullOrWhiteSpace(lastVersion) ? null : lastVersion,
                FirstSeenAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            inst.LastSeenAt = now;
            if (channel is not null) { inst.Channel = channel; }
            if (!string.IsNullOrWhiteSpace(fingerprint)) { inst.MachineFingerprintHash = fingerprint; }
            if (!string.IsNullOrWhiteSpace(lastVersion)) { inst.LastVersion = lastVersion; }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 并发同键 upsert：放弃本次写入（活跃心跳类数据，下一次上报会追上）
            foreach (var entry in db.ChangeTracker.Entries<Installation>().Where(e => e.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>幂等插入 InstallationReport；返回 true = 已存在（重复上报/补报）。</summary>
    private static async Task<bool> TryInsertInstallReportAsync(AtomUpdateDbContext db, InstallationReport report,
        CancellationToken ct)
    {
        // SQLite 唯一索引恒 NULLS DISTINCT（docs/11 §3.3）：成功上报的 ErrorCode 为 NULL，重复行不会被索引拦截，
        // 应用层先查补偿保证重复上报仍返回 Duplicate=true；pg 由 NULLS NOT DISTINCT 唯一索引兜底，免一查
        if (db.Database.IsSqlite() && await db.InstallationReports.IgnoreQueryFilters().AnyAsync(r =>
                r.AppId == report.AppId
                && r.InstallationId == report.InstallationId
                && r.ToVersion == report.ToVersion
                && r.Stage == report.Stage
                && r.ErrorCode == report.ErrorCode, ct))
        {
            return true;
        }

        db.InstallationReports.Add(report);
        try
        {
            await db.SaveChangesAsync(ct);
            return false;
        }
        catch (DbUpdateException)
        {
            // 唯一索引 (AppId, InstallationId, ToVersion, Stage, ErrorCode) 冲突 = 直传与补报重复
            db.Entry(report).State = EntityState.Detached;
            return true;
        }
    }

    /// <summary>入队下载日志；installationId 缺失/超长时丢弃并返回 false。</summary>
    private static bool EnqueueDownload(ReportQueue queue, App app, DownloadReport request)
    {
        if (string.IsNullOrWhiteSpace(request.InstallationId) || request.InstallationId.Length > 64)
        {
            return false;
        }

        queue.Enqueue(new ReportQueueItem
        {
            Download = new DownloadLog
            {
                TenantId = app.TenantId,
                AppId = app.Id,
                InstallationId = request.InstallationId,
                FromVersion = request.FromVersion,
                ToVersion = request.ToVersion,
                Kind = request.Kind,
                Success = request.Success,
                Bytes = request.Bytes,
                DurationMs = request.DurationMs,
                ErrorCode = request.ErrorCode,
                DownloadedAt = request.Timestamp
            }
        });
        return true;
    }

    private static InstallationReport ToEntity(App app, InstallReport request) => new()
    {
        TenantId = app.TenantId,
        AppId = app.Id,
        InstallationId = request.InstallationId,
        FromVersion = request.FromVersion,
        ToVersion = request.ToVersion,
        Stage = request.Stage,
        InstallMode = request.InstallMode,
        Elevated = request.Elevated,
        ElevationRejected = request.ElevationRejected,
        ErrorCode = request.ErrorCode,
        ErrorMessage = request.ErrorMessage is { Length: > 2000 } m ? m[..2000] : request.ErrorMessage,
        ReportedAt = request.Timestamp
    };
}

/// <summary>安装实例注册（/api/stats/install；SDK 当前走 batch startup，此端点供直接集成）。</summary>
public sealed class StatsInstallRequest
{
    public required string ProjectKey { get; init; }
    public required string AppId { get; init; }
    public required string InstallationId { get; init; }
    public string? Version { get; init; }
    public UpdateChannel? Channel { get; init; }
}

public sealed class ReportInstallResponse
{
    public bool Accepted { get; init; }
    public bool Duplicate { get; init; }
}

public sealed class StatsBatchResponse
{
    public int Accepted { get; init; }
    public int Skipped { get; init; }
}
