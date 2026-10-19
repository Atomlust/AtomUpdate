using AtomUpdate.Api.Data;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Endpoints.Manage;

/// <summary>管理端跨应用版本列表：默认全量（版本页进入即可见所有版本），
/// projectId/appId/keyword 可选检索（选项目不选应用 → 该项目全部应用的版本）。
/// Release 走租户全局查询过滤器，与其它 manage 端点一致的隔离口径。</summary>
internal static class ReleaseListEndpoint
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/releases", ListAsync)
            .Produces<PagedResult<ManageReleaseDto>>();
    }

    private static async Task<IResult> ListAsync(AtomUpdateDbContext db, long? projectId, long? appId,
        int? page, int? pageSize, string? keyword, CancellationToken ct)
    {
        var query = db.Releases.AsNoTracking().AsQueryable();
        if (projectId is { } pid)
        {
            query = query.Where(r => db.Apps.Any(a => a.Id == r.AppId && a.ProjectId == pid));
        }

        if (appId is { } aid)
        {
            query = query.Where(r => r.AppId == aid);
        }

        if (keyword?.Trim() is { Length: > 0 } kw)
        {
            // npgsql→ILike；SQLite 无 ILike 翻译，退化 Like（其 LIKE 对 ASCII 默认不区分大小写，行为对齐）
            query = db.Database.IsSqlite()
                ? query.Where(r => EF.Functions.Like(r.Version, $"%{kw}%"))
                : query.Where(r => EF.Functions.ILike(r.Version, $"%{kw}%"));
        }

        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        var ids = items.Select(r => r.Id).ToList();
        var assets = await db.ReleaseAssets.AsNoTracking().Where(a => ids.Contains(a.ReleaseId))
            .OrderBy(a => a.Id).ToListAsync(ct);

        var appIds = items.Select(r => r.AppId).Distinct().ToList();
        var appRows = await db.Apps.AsNoTracking().Where(a => appIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var projectIds = appRows.Values.Select(a => a.ProjectId).Distinct().ToList();
        var projectRows = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return Results.Ok(new PagedResult<ManageReleaseDto>
        {
            Items = items.Select(r =>
            {
                var appRow = appRows[r.AppId];
                return new ManageReleaseDto
                {
                    Id = r.Id,
                    Version = r.Version,
                    BuildNumber = r.BuildNumber,
                    Status = r.Status,
                    ForceUpdate = r.ForceUpdate,
                    PublishedAt = r.PublishedAt,
                    CreatedAt = r.CreatedAt,
                    Assets = assets.Where(a => a.ReleaseId == r.Id).Select(a => new ReleaseAssetDto
                    {
                        Id = a.Id,
                        Kind = a.Kind,
                        FromVersion = a.FromVersion,
                        Arch = a.Arch,
                        PackageType = a.PackageType,
                        Url = a.Url,
                        Size = a.Size,
                        Sha256 = a.Sha256,
                        Signature = a.Signature,
                        KeyId = a.KeyId,
                        SilentArgs = a.SilentArgs,
                        InstallArgs = a.InstallArgs
                    }).ToList(),
                    ProjectId = appRow.ProjectId,
                    AppId = r.AppId,
                    ProjectName = projectRows[appRow.ProjectId].Name,
                    AppName = appRow.Name,
                    AppBusinessId = appRow.AppId,
                    Platform = appRow.Platform.ToString().ToLowerInvariant()
                };
            }).ToList(),
            Total = total
        });
    }
}
