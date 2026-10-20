using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Endpoints.Manage;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NuGet.Versioning;

namespace AtomUpdate.Api.Endpoints.Releases;

/// <summary>发布生命周期端点（Packager 登录会话 / 管理后台使用，Manage 策略 JWT，见 03 §3.4 / 05 §11）。</summary>
internal static class ReleaseEndpoints
{
    private const string GroupPattern = "/api/projects/{projectId:long}/apps/{appId:long}/releases";

    internal static void MapReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPattern)
            .RequireAuthorization(AuthPolicies.Manage)
            .WithTags("releases");

        group.MapPost("", CreateAsync)
            .Produces<ReleaseDto>(StatusCodes.Status201Created)
            .Produces<ReleaseDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:long}/publish", PublishAsync)
            .Produces<ReleaseDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:long}/unpublish", UnpublishAsync)
            .Produces<ReleaseDto>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:long}", UpdateAsync)
            .Produces<ReleaseDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("", ListAsync)
            .Produces<PagedResult<ReleaseDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:long}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CreateAsync(long projectId, long appId, ReleaseCreateRequest request,
        IValidator<ReleaseCreateRequest> validator, AtomUpdateDbContext db, TenantContext tenantContext,
        HttpContext http, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        if (tenantContext.TenantId is not { } tenantId)
        {
            return ManageHelpers.TenantRequired();
        }

        var (app, error) = await ResolveTargetAsync(projectId, appId, db, ct);
        if (app is null)
        {
            return error!;
        }

        var parsed = SemanticVersion.Parse(request.Version);
        var version = parsed.ToNormalizedString();
        var assets = request.Assets.Select(Normalize).ToList();

        var existing = await FindReleaseAsync(db, app.Id, version, ct);
        if (existing is not null)
        {
            return await RespondForExistingAsync(db, existing, assets, ct);
        }

        var release = new Release
        {
            TenantId = tenantId,
            AppId = app.Id,
            Version = version,
            VersionMajor = parsed.Major,
            VersionMinor = parsed.Minor,
            VersionPatch = parsed.Patch,
            BuildNumber = request.BuildNumber,
            Status = request.Status ?? ReleaseStatus.Draft,
            ForceUpdate = request.ForceUpdate,
            MinSupportedVersion = request.MinSupportedVersion,
            CreatedBy = ManageHelpers.ActorOf(http),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Releases.Add(release);

        try
        {
            // 两段保存：先落 Release 拿到 IdGen 主键，再落资产/说明并审计
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // 并发同版本创建：唯一索引兜底，回源走幂等比较
            db.ChangeTracker.Clear();
            var raced = await FindReleaseAsync(db, app.Id, version, ct);
            return raced is null
                ? ManageHelpers.Conflict("版本创建冲突，请重试")
                : await RespondForExistingAsync(db, raced, assets, ct);
        }

        foreach (var asset in assets)
        {
            db.ReleaseAssets.Add(new ReleaseAsset
            {
                TenantId = tenantId,
                ReleaseId = release.Id,
                Kind = asset.Kind,
                FromVersion = asset.FromVersion,
                Arch = asset.Arch,
                PackageType = asset.PackageType,
                Url = asset.Url,
                Size = asset.Size,
                Sha256 = asset.Sha256,
                Signature = asset.Signature,
                KeyId = asset.KeyId,
                SilentArgs = asset.SilentArgs,
                InstallArgs = asset.InstallArgs,
                SourceId = asset.SourceId
            });
        }

        if (request.Notes is { } notes)
        {
            db.ReleaseNotes.Add(new ReleaseNote
            {
                TenantId = tenantId,
                ReleaseId = release.Id,
                Language = notes.Language,
                New = [.. notes.New],
                Improvements = [.. notes.Improvements],
                Fixes = [.. notes.Fixes],
                Notes = [.. notes.Notes],
                RawMarkdown = notes.RawMarkdown
            });
        }

        ManageHelpers.Audit(db, tenantId, ManageHelpers.ActorOf(http), "release.create", "Release", release.Id,
            new
            {
                version = release.Version,
                platform = app.Platform,
                archs = assets.Select(a => a.Arch).Distinct().ToList(),
                buildNumber = release.BuildNumber,
                assets = assets.Count
            });
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/projects/{projectId}/apps/{appId}/releases/{release.Id}",
            await LoadDtoAsync(db, release, ct));
    }

    private static async Task<IResult> PublishAsync(long projectId, long appId, long id, ReleasePublishRequest request,
        IValidator<ReleasePublishRequest> validator, AtomUpdateDbContext db, HttpContext http, IReleaseCache cache,
        IReadCache readCache, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        var (app, error) = await ResolveTargetAsync(projectId, appId, db, ct);
        if (app is null)
        {
            return error!;
        }

        var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == id && r.AppId == app.Id, ct);
        if (release is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ReleaseNotFound, "版本不存在");
        }

        if (release.Status == ReleaseStatus.Withdrawn)
        {
            return Results.Json(new ErrorResponse
            {
                Code = ErrorCodes.ReleaseWithdrawn,
                Message = "版本已撤回，禁止同版本重新发布，请使用新构建号重发"
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var publishedAt = request.PublishedAt ?? now;
        var rows = await db.ReleaseChannels.Where(rc => rc.ReleaseId == release.Id).ToListAsync(ct);

        foreach (var channel in request.Channels)
        {
            var row = rows.FirstOrDefault(x => x.Channel == channel);
            if (row is null)
            {
                db.ReleaseChannels.Add(new ReleaseChannel
                {
                    ReleaseId = release.Id,
                    Channel = channel,
                    Status = ReleaseChannelStatus.Published,
                    PublishedAt = publishedAt,
                    ReleaseSeq = 1
                });
            }
            else if (row.Status != ReleaseChannelStatus.Published)
            {
                // bump 序号即缓存失效（02 §3.1），重复发布会跳过（幂等）
                row.Status = ReleaseChannelStatus.Published;
                row.PublishedAt = publishedAt;
                row.ReleaseSeq++;
            }
        }

        if (release.Status != ReleaseStatus.Published)
        {
            release.Status = ReleaseStatus.Published;
        }

        release.PublishedAt ??= publishedAt;
        release.UpdatedAt = now;

        ManageHelpers.Audit(db, release.TenantId, ManageHelpers.ActorOf(http), "release.publish", "Release",
            release.Id, new
            {
                version = release.Version,
                channels = request.Channels
            });
        await db.SaveChangesAsync(ct);
        cache.Invalidate(app.Id);
        readCache.InvalidateAll(); // 概览统计含发布版本数，发布即失效

        return Results.Ok(await LoadDtoAsync(db, release, ct));
    }

    private static async Task<IResult> UpdateAsync(long projectId, long appId, long id, ReleaseUpdateRequest request,
        IValidator<ReleaseUpdateRequest> validator, AtomUpdateDbContext db, TenantContext tenantContext,
        HttpContext http, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        if (tenantContext.TenantId is not { } tenantId)
        {
            return ManageHelpers.TenantRequired();
        }

        var (app, error) = await ResolveTargetAsync(projectId, appId, db, ct);
        if (app is null)
        {
            return error!;
        }

        var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == id && r.AppId == app.Id, ct);
        if (release is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ReleaseNotFound, "版本不存在");
        }

        // 已发布版本不可编辑（变更走新构建号）；已撤回版本禁止任何变动
        if (release.Status is not (ReleaseStatus.Draft or ReleaseStatus.Prerelease))
        {
            return Results.Json(new ErrorResponse
            {
                Code = ErrorCodes.Conflict,
                Message = "仅草稿/预发布版本可编辑"
            }, statusCode: StatusCodes.Status409Conflict);
        }

        if (request.BuildNumber is { } buildNumber)
        {
            release.BuildNumber = buildNumber;
        }

        if (request.ForceUpdate is { } forceUpdate)
        {
            release.ForceUpdate = forceUpdate;
        }

        if (!string.IsNullOrEmpty(request.MinSupportedVersion))
        {
            release.MinSupportedVersion = request.MinSupportedVersion;
        }

        if (request.Notes is { } notes)
        {
            var existingNotes = await db.ReleaseNotes.Where(n => n.ReleaseId == release.Id).ToListAsync(ct);
            db.ReleaseNotes.RemoveRange(existingNotes);
            db.ReleaseNotes.Add(new ReleaseNote
            {
                TenantId = tenantId,
                ReleaseId = release.Id,
                Language = notes.Language,
                New = [.. notes.New],
                Improvements = [.. notes.Improvements],
                Fixes = [.. notes.Fixes],
                Notes = [.. notes.Notes],
                RawMarkdown = notes.RawMarkdown
            });
        }

        release.UpdatedAt = DateTimeOffset.UtcNow;
        ManageHelpers.Audit(db, tenantId, ManageHelpers.ActorOf(http), "release.update", "Release", release.Id,
            new { version = release.Version, platform = app.Platform });
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadDtoAsync(db, release, ct));
    }

    private static async Task<IResult> UnpublishAsync(long projectId, long appId, long id,
        AtomUpdateDbContext db, HttpContext http, IReleaseCache cache, IReadCache readCache, CancellationToken ct)
    {
        var (app, error) = await ResolveTargetAsync(projectId, appId, db, ct);
        if (app is null)
        {
            return error!;
        }

        var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == id && r.AppId == app.Id, ct);
        if (release is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ReleaseNotFound, "版本不存在");
        }

        var now = DateTimeOffset.UtcNow;
        var changed = false;

        var rows = await db.ReleaseChannels.Where(rc => rc.ReleaseId == release.Id).ToListAsync(ct);
        foreach (var row in rows.Where(r => r.Status != ReleaseChannelStatus.Withdrawn))
        {
            row.Status = ReleaseChannelStatus.Withdrawn;
            row.ReleaseSeq++;
            changed = true;
        }

        if (release.Status != ReleaseStatus.Withdrawn)
        {
            release.Status = ReleaseStatus.Withdrawn;
            release.UpdatedAt = now;
            changed = true;
        }

        if (changed)
        {
            ManageHelpers.Audit(db, release.TenantId, ManageHelpers.ActorOf(http), "release.unpublish", "Release",
                release.Id, new { version = release.Version });
            await db.SaveChangesAsync(ct);
            cache.Invalidate(app.Id);
            readCache.InvalidateAll(); // 概览统计含发布版本数，撤回即失效
        }

        return Results.Ok(await LoadDtoAsync(db, release, ct));
    }

    /// <summary>受限删除：仅草稿/已撤回版本可物理删除（发布中/预发布必须先撤回，避免打断在线客户端更新链路）。
    /// 级联清理资产、通道与说明记录；本服务 /uploads 存储文件一并删除（外部 URL 跳过），并写审计日志。</summary>
    private static async Task<IResult> DeleteAsync(long projectId, long appId, long id,
        AtomUpdateDbContext db, HttpContext http, IReleaseCache cache, IReadCache readCache, IConfiguration config,
        CancellationToken ct)
    {
        var (app, error) = await ResolveTargetAsync(projectId, appId, db, ct);
        if (app is null)
        {
            return error!;
        }

        var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == id && r.AppId == app.Id, ct);
        if (release is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ReleaseNotFound, "版本不存在");
        }

        if (release.Status is not (ReleaseStatus.Draft or ReleaseStatus.Withdrawn))
        {
            return ManageHelpers.Conflict($"版本 {release.Version} 处于 {release.Status} 状态，请先撤回再删除");
        }

        // 清理本服务存储的文件：资产 URL 形如 {host}/uploads/{appId}/{version}/{file}，外部 URL 不动
        var assets = await db.ReleaseAssets.Where(a => a.ReleaseId == release.Id).ToListAsync(ct);
        var root = ManageHelpers.ResolveStorageRoot(config,
            http.RequestServices.GetRequiredService<IWebHostEnvironment>());
        var safeApp = app.AppId.Replace('/', '_').Replace('\\', '_');
        var versionDir = Path.GetFullPath(Path.Combine(root, safeApp, release.Version));
        var marker = $"/uploads/{safeApp}/{release.Version}/";
        foreach (var url in assets.Select(a => a.Url))
        {
            var idx = url.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                continue;
            }

            var filename = Uri.UnescapeDataString(url[(idx + marker.Length)..].Split('?')[0]);
            var full = Path.GetFullPath(Path.Combine(versionDir, filename));
            if (full.StartsWith(versionDir, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            {
                File.Delete(full);
            }
        }

        if (Directory.Exists(versionDir) && !Directory.EnumerateFileSystemEntries(versionDir).Any())
        {
            Directory.Delete(versionDir);
        }

        var channels = await db.ReleaseChannels.Where(rc => rc.ReleaseId == release.Id).ToListAsync(ct);
        var notes = await db.ReleaseNotes.Where(n => n.ReleaseId == release.Id).ToListAsync(ct);
        db.ReleaseAssets.RemoveRange(assets);
        db.ReleaseChannels.RemoveRange(channels);
        db.ReleaseNotes.RemoveRange(notes);
        db.Releases.Remove(release);

        ManageHelpers.Audit(db, release.TenantId, ManageHelpers.ActorOf(http), "release.delete", "Release",
            release.Id, new { version = release.Version, files = assets.Count });
        await db.SaveChangesAsync(ct);
        cache.Invalidate(app.Id);
        readCache.InvalidateAll(); // 概览统计含发布版本数，删除即失效

        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(long projectId, long appId, AtomUpdateDbContext db,
        HttpContext http, int? page, int? pageSize, string? arch,
        string? status, CancellationToken ct)
    {
        var (app, error) = await ResolveTargetAsync(projectId, appId, db, ct);
        if (app is null)
        {
            return error!;
        }

        // 枚举过滤参数按契约 camelCase 解析（框架内置绑定区分大小写，会拒绝 "macos"）
        if (ParseFilter("arch", arch, out UpdateArch? archFilter) is { } archError)
        {
            return archError;
        }

        if (ParseFilter("status", status, out ReleaseStatus? statusFilter) is { } statusError)
        {
            return statusError;
        }

        var query = db.Releases.AsNoTracking().Where(r => r.AppId == app.Id);
        if (archFilter is not null)
        {
            // 架构在各资产上：筛"有该架构包的版本"
            query = query.Where(r => db.ReleaseAssets.Any(a => a.ReleaseId == r.Id && a.Arch == archFilter));
        }

        if (statusFilter is not null)
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.VersionMajor).ThenByDescending(r => r.VersionMinor)
            .ThenByDescending(r => r.VersionPatch).ThenByDescending(r => r.CreatedAt)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        var ids = items.Select(r => r.Id).ToList();
        var assets = await db.ReleaseAssets.AsNoTracking().Where(a => ids.Contains(a.ReleaseId))
            .OrderBy(a => a.Id).ToListAsync(ct);
        var notes = await db.ReleaseNotes.AsNoTracking().Where(n => ids.Contains(n.ReleaseId))
            .OrderBy(n => n.Id).ToListAsync(ct);
        var rows = await db.ReleaseChannels.AsNoTracking().Where(rc => ids.Contains(rc.ReleaseId)).ToListAsync(ct);

        return Results.Ok(new PagedResult<ReleaseDto>
        {
            Items = items.Select(r => ToDto(r,
                assets.Where(a => a.ReleaseId == r.Id).ToList(),
                notes.FirstOrDefault(n => n.ReleaseId == r.Id),
                rows.Where(rc => rc.ReleaseId == r.Id).ToList())).ToList(),
            Total = total
        });
    }

    /// <summary>定位项目/应用（Manage 会话）：项目可见性由租户全局查询过滤器隔离，应用必须挂在该项目下。</summary>
    private static async Task<(App? App, IResult? Error)> ResolveTargetAsync(long projectId, long appId,
        AtomUpdateDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            return (null, ManageHelpers.NotFound(ErrorCodes.ProjectNotFound, "项目不存在"));
        }

        var app = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId && a.ProjectId == projectId, ct);
        return app is null
            ? (null, ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在"))
            : (app, null);
    }

    private static IResult? ParseFilter<TEnum>(string name, string? value, out TEnum? filter)
        where TEnum : struct, Enum
    {
        filter = null;
        if (value is null)
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            filter = parsed;
            return null;
        }

        return Results.BadRequest(new ErrorResponse
        {
            Code = ErrorCodes.InvalidRequest,
            Message = $"{name} 取值非法：{value}"
        });
    }

    private static Task<Release?> FindReleaseAsync(AtomUpdateDbContext db, long appId, string version,
        CancellationToken ct) =>
        db.Releases.FirstOrDefaultAsync(r => r.AppId == appId && r.Version == version, ct);

    /// <summary>同版本已存在：资产逐项一致 → 幂等 200；否则 409（禁止同版本重传）。</summary>
    private static async Task<IResult> RespondForExistingAsync(AtomUpdateDbContext db, Release existing,
        IReadOnlyList<NormalizedAsset> assets, CancellationToken ct)
    {
        if (existing.Status == ReleaseStatus.Withdrawn)
        {
            return Results.Json(new ErrorResponse
            {
                Code = ErrorCodes.ReleaseExists,
                Message = $"版本 {existing.Version} 已撤回，禁止同版本重传，请使用新构建号"
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var stored = await db.ReleaseAssets.AsNoTracking().Where(a => a.ReleaseId == existing.Id).ToListAsync(ct);
        var storedIdentities = stored
            .Select(a => IdentityKey(a.Kind, a.FromVersion, a.Arch, a.PackageType, a.Url, a.Sha256))
            .OrderBy(x => x, StringComparer.Ordinal);
        var incomingIdentities = assets
            .Select(a => IdentityKey(a.Kind, a.FromVersion, a.Arch, a.PackageType, a.Url, a.Sha256))
            .OrderBy(x => x, StringComparer.Ordinal);

        if (!storedIdentities.SequenceEqual(incomingIdentities))
        {
            return Results.Json(new ErrorResponse
            {
                Code = ErrorCodes.ReleaseExists,
                Message = $"版本 {existing.Version} 已存在且资产不一致，禁止同版本重传，请使用新构建号"
            }, statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(await LoadDtoAsync(db, existing, ct));
    }

    private static string IdentityKey(AssetKind kind, string? fromVersion, UpdateArch arch, PackageType packageType,
        string url, string sha256) =>
        $"{kind}\u001f{fromVersion}\u001f{arch}\u001f{packageType}\u001f{url}\u001f{sha256.ToLowerInvariant()}";

    private sealed record NormalizedAsset(
        AssetKind Kind, string? FromVersion, UpdateArch Arch, PackageType PackageType, string Url, long? Size,
        string Sha256, string? Signature, string? KeyId, string? SilentArgs, string? InstallArgs, long? SourceId);

    private static NormalizedAsset Normalize(ReleaseAssetInput a) => new(
        a.Kind,
        a.FromVersion is null ? null : SemanticVersion.Parse(a.FromVersion).ToNormalizedString(),
        a.Arch,
        a.PackageType,
        a.Url,
        a.Size,
        a.Sha256.ToLowerInvariant(),
        a.Signature,
        a.KeyId,
        a.SilentArgs,
        a.InstallArgs,
        a.SourceId);

    private static async Task<ReleaseDto> LoadDtoAsync(AtomUpdateDbContext db, Release release, CancellationToken ct)
    {
        var assets = await db.ReleaseAssets.AsNoTracking().Where(a => a.ReleaseId == release.Id)
            .OrderBy(a => a.Id).ToListAsync(ct);
        var note = await db.ReleaseNotes.AsNoTracking().Where(n => n.ReleaseId == release.Id)
            .OrderBy(n => n.Id).FirstOrDefaultAsync(ct);
        var rows = await db.ReleaseChannels.AsNoTracking().Where(rc => rc.ReleaseId == release.Id).ToListAsync(ct);
        return ToDto(release, assets, note, rows);
    }

    private static ReleaseDto ToDto(Release release, IReadOnlyList<ReleaseAsset> assets, ReleaseNote? note,
        IReadOnlyList<ReleaseChannel> channelRows)
    {
        var channels = channelRows
            .Select(row => new ReleaseChannelDto
            {
                Channel = row.Channel,
                Status = row.Status,
                PublishedAt = row.PublishedAt,
                ReleaseSeq = row.ReleaseSeq
            })
            .ToList();

        return new ReleaseDto
        {
            Id = release.Id,
            Version = release.Version,
            BuildNumber = release.BuildNumber,
            Status = release.Status,
            ForceUpdate = release.ForceUpdate,
            MinSupportedVersion = release.MinSupportedVersion,
            PublishedAt = release.PublishedAt,
            CreatedAt = release.CreatedAt,
            Assets = assets.Select(a => new ReleaseAssetDto
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
            Notes = note is null
                ? null
                : new ReleaseNoteDto
                {
                    Language = note.Language,
                    New = note.New,
                    Improvements = note.Improvements,
                    Fixes = note.Fixes,
                    Notes = note.Notes,
                    RawMarkdown = note.RawMarkdown
                },
            Channels = channels
        };
    }
}
