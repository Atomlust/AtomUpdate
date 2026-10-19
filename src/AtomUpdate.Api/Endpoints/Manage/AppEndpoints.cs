using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PolicyEntity = AtomUpdate.Api.Data.Entities.UpdatePolicy;

namespace AtomUpdate.Api.Endpoints.Manage;

internal static class AppEndpoints
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/projects/{projectId:long}/apps", ListAsync)
            .Produces<PagedResult<AppDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapPost("/projects/{projectId:long}/apps", CreateAsync)
            .Produces<AppDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
        group.MapGet("/apps", ListAcrossProjectsAsync)
            .Produces<PagedResult<AppDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);
        group.MapGet("/apps/{id:long}", GetAsync)
            .Produces<AppDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapPatch("/apps/{id:long}", UpdateAsync)
            .Produces<AppDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapDelete("/projects/{projectId:long}/apps/{appId:long}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/apps/{id:long}/policy", GetPolicyAsync)
            .Produces<PolicyDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapPatch("/apps/{id:long}/policy", UpdatePolicyAsync)
            .Produces<PolicyDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(long projectId, AtomUpdateDbContext db, int? page, int? pageSize,
        CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId, ct);
        if (project is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ProjectNotFound, "项目不存在");
        }

        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.Apps.AsNoTracking().Where(a => a.ProjectId == projectId).OrderBy(a => a.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<AppDto>
        {
            Items = items.Select(a => ToDto(a, project.Name)).ToList(), Total = total
        });
    }

    /// <summary>跨项目应用列表（后台"应用"页默认视图）：项目/类型/名称关键字可选过滤，租户隔离由全局查询过滤器保证。</summary>
    private static async Task<IResult> ListAcrossProjectsAsync(AtomUpdateDbContext db, int? page, int? pageSize,
        long? projectId, string? type, string? keyword, CancellationToken ct)
    {
        AppType? appType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse(type, ignoreCase: true, out AppType parsedType))
            {
                return Results.BadRequest(new ErrorResponse
                {
                    Code = ErrorCodes.InvalidRequest,
                    Message = $"type 不是有效的应用类型：{type}"
                });
            }

            appType = parsedType;
        }

        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.Apps.AsNoTracking().AsQueryable();

        if (projectId is { } pid)
        {
            query = query.Where(a => a.ProjectId == pid);
        }

        if (appType is { } t)
        {
            query = query.Where(a => a.Type == t);
        }

        if (keyword?.Trim() is { Length: > 0 } kw)
        {
            var pattern = $"%{kw}%";
            // npgsql→ILike；SQLite 无 ILike 翻译，退化 Like（其 LIKE 对 ASCII 默认不区分大小写，行为对齐）
            query = db.Database.IsSqlite()
                ? query.Where(a => EF.Functions.Like(a.Name, pattern) || EF.Functions.Like(a.AppId, pattern))
                : query.Where(a => EF.Functions.ILike(a.Name, pattern) || EF.Functions.ILike(a.AppId, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var projectIds = items.Select(a => a.ProjectId).Distinct().ToList();
        var projectNames = await db.Projects.AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return Results.Ok(new PagedResult<AppDto>
        {
            Items = items.Select(a => ToDto(a, projectNames.GetValueOrDefault(a.ProjectId))).ToList(),
            Total = total
        });
    }

    private static async Task<IResult> CreateAsync(long projectId, AppCreateRequest request,
        IValidator<AppCreateRequest> validator, AtomUpdateDbContext db, TenantContext tenantContext,
        HttpContext http, IReadCache readCache, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        if (tenantContext.TenantId is not { } tenantId)
        {
            return ManageHelpers.TenantRequired();
        }

        if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct))
        {
            return ManageHelpers.NotFound(ErrorCodes.ProjectNotFound, "项目不存在");
        }

        if (await db.Apps.AnyAsync(a => a.ProjectId == projectId && a.AppId == request.AppId, ct))
        {
            return ManageHelpers.Conflict("应用标识（appId）已存在");
        }

        var now = DateTimeOffset.UtcNow;
        var app = new App
        {
            TenantId = tenantId,
            ProjectId = projectId,
            AppId = request.AppId,
            Name = request.Name,
            Type = request.Type ?? AppType.App,
            Platform = request.Platform!.Value,
            Status = EntityStatus.Active,
            CreatedAt = now
        };
        db.Apps.Add(app);
        await db.SaveChangesAsync(ct);

        // 应用创建即生成默认策略，保证 /api/check 始终可读到策略行
        db.UpdatePolicies.Add(new PolicyEntity
        {
            TenantId = tenantId,
            AppId = app.Id,
            Version = 1,
            UpdatedAt = now,
            UpdatedBy = ManageHelpers.ActorOf(http)
        });
        ManageHelpers.Audit(db, tenantId, ManageHelpers.ActorOf(http), "app.create", "App", app.Id,
            new { appId = app.AppId });
        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // 同 appId 可能已产生解析负缓存，创建后即时失效

        return Results.Created($"/api/manage/apps/{app.Id}", ToDto(app));
    }

    private static async Task<IResult> GetAsync(long id, AtomUpdateDbContext db, CancellationToken ct)
    {
        var app = await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return app is null
            ? ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在")
            : Results.Ok(ToDto(app));
    }

    private static async Task<IResult> UpdateAsync(long id, AppUpdateRequest request,
        IValidator<AppUpdateRequest> validator, AtomUpdateDbContext db, HttpContext http,
        IReadCache readCache, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        var app = await db.Apps.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在");
        }

        if (request.Name is { } name)
        {
            app.Name = name;
        }

        if (request.Type is { } type)
        {
            app.Type = type;
        }

        if (request.Status is { } status)
        {
            app.Status = status;
        }

        app.UpdatedAt = DateTimeOffset.UtcNow;
        ManageHelpers.Audit(db, app.TenantId, ManageHelpers.ActorOf(http), "app.update", "App", app.Id);
        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // 应用状态（Active/Disabled）参与解析谓词，变更后即时失效

        return Results.Ok(ToDto(app));
    }

    /// <summary>受限删除：仅当应用下无任何版本记录时可删（防止在线客户端更新链路断裂，含草稿/撤回态）；
    /// 级联清理更新策略、应用级 API Key 与存储残余目录（版本删除已清文件，这里兜底删空目录），并写审计。</summary>
    private static async Task<IResult> DeleteAsync(long projectId, long appId, AtomUpdateDbContext db,
        HttpContext http, IReleaseCache cache, IReadCache readCache, IConfiguration config, CancellationToken ct)
    {
        var app = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId && a.ProjectId == projectId, ct);
        if (app is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在");
        }

        var releaseCount = await db.Releases.CountAsync(r => r.AppId == app.Id, ct);
        if (releaseCount > 0)
        {
            return ManageHelpers.Conflict($"应用下还有 {releaseCount} 个版本记录，请先删除全部版本");
        }

        var policies = await db.UpdatePolicies.Where(p => p.AppId == app.Id).ToListAsync(ct);
        db.UpdatePolicies.RemoveRange(policies);
        db.Apps.Remove(app);

        // 存储残余目录兜底清理：仅删空目录（版本文件已随版本删除清理）
        var root = ManageHelpers.ResolveStorageRoot(config,
            http.RequestServices.GetRequiredService<IWebHostEnvironment>());
        var dir = Path.Combine(root, app.AppId.Replace('/', '_').Replace('\\', '_'));
        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
        }

        ManageHelpers.Audit(db, app.TenantId, ManageHelpers.ActorOf(http), "app.delete", "App", app.Id,
            new { appId = app.AppId, policies = policies.Count });
        await db.SaveChangesAsync(ct);
        cache.Invalidate(app.Id);
        readCache.InvalidateAll(); // 解析缓存与概览统计即时失效

        return Results.NoContent();
    }

    private static async Task<IResult> GetPolicyAsync(long id, AtomUpdateDbContext db, TenantContext tenantContext,
        IReadCache readCache, CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return ManageHelpers.TenantRequired();
        }

        if (!await db.Apps.AnyAsync(a => a.Id == id, ct))
        {
            return ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在");
        }

        var policy = await EnsurePolicyAsync(db, tenantId, id, readCache, ct);
        return Results.Ok(ToDto(policy));
    }

    private static async Task<IResult> UpdatePolicyAsync(long id, PolicyUpdateRequest request,
        IValidator<PolicyUpdateRequest> validator, AtomUpdateDbContext db, TenantContext tenantContext,
        HttpContext http, IReadCache readCache, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        if (tenantContext.TenantId is not { } tenantId)
        {
            return ManageHelpers.TenantRequired();
        }

        if (!await db.Apps.AnyAsync(a => a.Id == id, ct))
        {
            return ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在");
        }

        var policy = await EnsurePolicyAsync(db, tenantId, id, readCache, ct);

        if (request.PollingEnabled is { } pollingEnabled)
        {
            policy.PollingEnabled = pollingEnabled;
        }

        if (request.PollingIntervalMinutes is { } pollingIntervalMinutes)
        {
            policy.PollingIntervalMinutes = pollingIntervalMinutes;
        }

        if (request.PollingJitterMinutes is { } pollingJitterMinutes)
        {
            policy.PollingJitterMinutes = pollingJitterMinutes;
        }

        if (request.DownloadOnStartup is { } downloadOnStartup)
        {
            policy.DownloadOnStartup = downloadOnStartup;
        }

        if (request.DownloadOnPolling is { } downloadOnPolling)
        {
            policy.DownloadOnPolling = downloadOnPolling;
        }

        if (request.DownloadOnManualCheck is { } downloadOnManualCheck)
        {
            policy.DownloadOnManualCheck = downloadOnManualCheck;
        }

        if (request.DownloadOnForceUpdate is { } downloadOnForceUpdate)
        {
            policy.DownloadOnForceUpdate = downloadOnForceUpdate;
        }

        if (request.DownloadOnNetworkRecovery is { } downloadOnNetworkRecovery)
        {
            policy.DownloadOnNetworkRecovery = downloadOnNetworkRecovery;
        }

        if (request.DownloadOnResume is { } downloadOnResume)
        {
            policy.DownloadOnResume = downloadOnResume;
        }

        if (request.InstallMode is { } installMode)
        {
            policy.InstallMode = installMode;
        }

        if (request.AllowForceUpdate is { } allowForceUpdate)
        {
            policy.AllowForceUpdate = allowForceUpdate;
        }

        if (request.AllowDowngrade is { } allowDowngrade)
        {
            policy.AllowDowngrade = allowDowngrade;
        }

        if (request.AllowPostpone is { } allowPostpone)
        {
            policy.AllowPostpone = allowPostpone;
        }

        if (request.MaxPostponeCount is { } maxPostponeCount)
        {
            policy.MaxPostponeCount = maxPostponeCount;
        }

        if (request.AllowMeteredNetwork is { } allowMeteredNetwork)
        {
            policy.AllowMeteredNetwork = allowMeteredNetwork;
        }

        if (request.MaxDownloadSpeedKBps is { } maxDownloadSpeedKBps)
        {
            policy.MaxDownloadSpeedKBps = maxDownloadSpeedKBps;
        }

        // 仅当确有字段变化才递增版本（版本驱动客户端策略分发与 ETag）
        if (db.Entry(policy).Properties.Any(p => p.IsModified))
        {
            policy.Version++;
            policy.UpdatedAt = DateTimeOffset.UtcNow;
            policy.UpdatedBy = ManageHelpers.ActorOf(http);
            ManageHelpers.Audit(db, tenantId, ManageHelpers.ActorOf(http), "policy.update", "UpdatePolicy", policy.Id,
                new { version = policy.Version });
        }

        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // 策略驱动 check 分发与 ETag，变更（含补建默认策略）后即时失效

        return Results.Ok(ToDto(policy));
    }

    private static async Task<PolicyEntity> EnsurePolicyAsync(AtomUpdateDbContext db, long tenantId, long appId,
        IReadCache readCache, CancellationToken ct)
    {
        var policy = await db.UpdatePolicies.FirstOrDefaultAsync(p => p.AppId == appId && p.Channel == null, ct);
        if (policy is not null)
        {
            return policy;
        }

        policy = new PolicyEntity
        {
            TenantId = tenantId,
            AppId = appId,
            Version = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.UpdatePolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // check 路径可能已缓存该应用的空策略，补建后即时失效
        return policy;
    }

    private static AppDto ToDto(App app, string? projectName = null) => new()
    {
        Id = app.Id,
        AppId = app.AppId,
        Name = app.Name,
        Type = app.Type,
        Platform = app.Platform,
        Status = app.Status,
        CreatedAt = app.CreatedAt,
        ProjectId = app.ProjectId,
        ProjectName = projectName
    };

    private static PolicyDto ToDto(PolicyEntity policy) => new()
    {
        Version = policy.Version,
        PollingEnabled = policy.PollingEnabled,
        PollingIntervalMinutes = policy.PollingIntervalMinutes,
        PollingJitterMinutes = policy.PollingJitterMinutes,
        DownloadOnStartup = policy.DownloadOnStartup,
        DownloadOnPolling = policy.DownloadOnPolling,
        DownloadOnManualCheck = policy.DownloadOnManualCheck,
        DownloadOnForceUpdate = policy.DownloadOnForceUpdate,
        DownloadOnNetworkRecovery = policy.DownloadOnNetworkRecovery,
        DownloadOnResume = policy.DownloadOnResume,
        InstallMode = policy.InstallMode,
        AllowForceUpdate = policy.AllowForceUpdate,
        AllowDowngrade = policy.AllowDowngrade,
        AllowPostpone = policy.AllowPostpone,
        MaxPostponeCount = policy.MaxPostponeCount,
        AllowMeteredNetwork = policy.AllowMeteredNetwork,
        MaxDownloadSpeedKBps = policy.MaxDownloadSpeedKBps,
        UpdatedAt = policy.UpdatedAt,
        UpdatedBy = policy.UpdatedBy
    };
}
