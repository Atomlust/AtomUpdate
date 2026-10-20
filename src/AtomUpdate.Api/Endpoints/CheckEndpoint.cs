using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Telemetry;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using PolicyEntity = AtomUpdate.Api.Data.Entities.UpdatePolicy;
using PolicyDto = AtomUpdate.Core.Models.UpdatePolicy;

namespace AtomUpdate.Api.Endpoints;

/// <summary>更新检查端点（03 §3）：公开访问（无需鉴权）/ 分层响应 / ETag 304 / 增量优先包选择 / 策略分发 / 异步检查日志。</summary>
internal static class CheckEndpoint
{
    internal static async Task<IResult> Handle(
        CheckRequest request,
        IValidator<CheckRequest> validator,
        AtomUpdateDbContext db,
        TenantContext tenantContext,
        IReleaseCache releaseCache,
        IReadCache readCache,
        CheckLogQueue logQueue,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var invalidVersion = validation.Errors.FirstOrDefault(e => e.ErrorCode == ErrorCodes.InvalidVersion);
            if (invalidVersion is not null)
            {
                return BadRequest(ErrorCodes.InvalidVersion, invalidVersion.ErrorMessage);
            }

            return BadRequest(ErrorCodes.InvalidRequest,
                string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        // 1) 定位项目与应用（解析读缓存，项目/应用/策略/发布变更 InvalidateAll 即时失效）：
        //    已带租户上下文（JWT）在租户内查；匿名跨租户按 Key 唯一命中才可确定租户（歧义 fail-closed）
        ProjectLookup lookup;
        if (tenantContext.TenantId is { } jwtTenant)
        {
            lookup = await readCache.GetAsync($"t:{jwtTenant}:proj:{request.ProjectKey}", async ct =>
                new ProjectLookup(await db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Key == request.ProjectKey && p.Status == EntityStatus.Active, ct),
                    false), cancellationToken);
        }
        else
        {
            lookup = await readCache.GetAsync($"anon:proj:{request.ProjectKey}", async ct =>
            {
                var matches = await db.Projects.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => p.Key == request.ProjectKey && p.Status == EntityStatus.Active)
                    .OrderBy(p => p.Id)
                    .Take(2)
                    .ToListAsync(ct);
                return new ProjectLookup(
                    matches.Count == 1 ? matches[0] : null,
                    Ambiguous: matches.Count > 1);
            }, cancellationToken);
        }

        if (lookup.Ambiguous)
        {
            return Unauthorized("projectKey 在多个租户下存在，无法确定租户");
        }

        if (lookup.Project is not { } project)
        {
            return NotFound(ErrorCodes.ProjectNotFound, $"项目不存在：{request.ProjectKey}");
        }

        // 匿名路径由解析结果确定租户，后续应用/策略查询经全局过滤器隔离
        tenantContext.TenantId ??= project.TenantId;
        var tenantId = tenantContext.TenantId.Value;

        var app = await readCache.GetAsync($"t:{tenantId}:app:{project.Id}:{request.AppId}", async ct =>
            await db.Apps.AsNoTracking().FirstOrDefaultAsync(
                a => a.ProjectId == project.Id && a.AppId == request.AppId && a.Status == EntityStatus.Active, ct),
            cancellationToken);
        if (app is null)
        {
            return NotFound(ErrorCodes.AppNotFound, $"应用不存在：{request.AppId}");
        }

        // 2) 平台守卫：平台属于应用，客户端上报的平台与应用注册平台不一致是配置错误，必须显式报错
        if (request.Platform != app.Platform)
        {
            return BadRequest(ErrorCodes.PlatformMismatch,
                $"应用 {app.AppId} 属于 {app.Platform.ToString().ToLowerInvariant()} 平台，" +
                $"但请求上报的是 {request.Platform.ToString().ToLowerInvariant()}，请检查 AppId/构建目标是否用错");
        }

        // 3) 通道：UpdateChannel 枚举，缺省 stable
        var channel = request.Channel ?? UpdateChannel.Stable;

        // 4) 策略分发：仅当客户端版本落后时才下发策略体（解析读缓存，策略变更端点失效）
        var policy = await readCache.GetAsync($"t:{tenantId}:policy:{app.Id}", async ct =>
            await db.UpdatePolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.AppId == app.Id && p.Channel == null, ct), cancellationToken);

        // 5) 最新已发布版本（读路径缓存，发布/撤回 bump epoch 失效；架构在资产上，缓存条目与架构无关）
        var latest = await releaseCache.GetLatestAsync(
            new ReleaseCacheKey(app.Id, channel),
            (ct) => LoadLatestAsync(db, app.Id, channel, ct),
            cancellationToken);

        var current = SemanticVersion.Parse(request.CurrentVersion);
        var currentNorm = current.ToNormalizedString();
        var latestVersion = latest?.Version ?? currentNorm;

        // 架构在各资产上：先按客户端架构筛出可用包，无对应包即视为无更新可发
        var archAssets = ForArch(latest?.Assets, request.Arch);

        int? comparison = latest is null ? null : SemanticVersion.Parse(latest.Version).CompareTo(current);
        var hasUpdate = (comparison is > 0 || (comparison is < 0 && policy is { AllowDowngrade: true })) &&
                        archAssets.Count > 0;
        var policyVersion = policy?.Version ?? 0;
        var policyUnchanged = request.PolicyVersion == policyVersion;

        // 6) ETag（03 §3.1）+ 304 快捷路径
        var etag = ComputeETag(project.Key, app.AppId, request.Platform, request.Arch, currentNorm, latestVersion,
            policyVersion);
        http.Response.Headers.ETag = etag;
        var etagHit = IsNotModified(http.Request.Headers[HeaderNames.IfNoneMatch].ToString(), etag);

        logQueue.Enqueue(new UpdateCheckLog
        {
            TenantId = app.TenantId,
            InstallationId = request.InstallationId,
            AppId = app.Id,
            ProjectKey = project.Key,
            AppKey = app.AppId,
            Platform = request.Platform,
            Arch = request.Arch,
            Channel = channel,
            CurrentVersion = currentNorm,
            LatestVersion = latestVersion,
            HasUpdate = hasUpdate,
            ETagHit = etagHit,
            PolicyUnchanged = policyUnchanged,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            CheckedAt = DateTimeOffset.UtcNow
        });

        if (etagHit)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        if (!hasUpdate)
        {
            return Results.Ok(new CheckResponse
            {
                HasUpdate = false,
                LatestVersion = latestVersion,
                PolicyUnchanged = policyUnchanged,
                Policy = policyUnchanged ? null : ToPolicyDto(policy)
            });
        }

        // 7) 增量优先包选择：FromVersion 命中当前版本选 delta，否则回落全量
        var packageAsset = BestDelta(archAssets, currentNorm) ?? BestFull(archAssets, preferZip: true);
        var fullPackageAsset = BestFull(archAssets, preferZip: false);

        var belowMin = latest!.MinSupportedVersion is { } minRaw &&
                       SemanticVersion.TryParse(minRaw, out var minSem) && current < minSem;
        var forceUpdate = policy is { AllowForceUpdate: true } && (latest.ForceUpdate || belowMin);

        // 发行说明随版本缓存（已发布版本不可编辑），命中后不再查说明表
        var note = latest?.Notes.FirstOrDefault(n => n.Language == "en") ?? latest?.Notes.FirstOrDefault();

        return Results.Ok(new CheckResponse
        {
            HasUpdate = true,
            LatestVersion = latest!.Version,
            ForceUpdate = forceUpdate,
            MinSupportedVersion = latest.MinSupportedVersion,
            PolicyUnchanged = policyUnchanged,
            Policy = policyUnchanged ? null : ToPolicyDto(policy),
            Package = packageAsset is null ? null : ToPackage(packageAsset, packageAsset.Kind),
            FullPackage = fullPackageAsset is null ? null : ToPackage(fullPackageAsset, null),
            ReleaseNotes = note is null
                ? null
                : new ReleaseNotes
                {
                    New = note.New,
                    Improvements = note.Improvements,
                    Fixes = note.Fixes,
                    Notes = note.Notes
                }
        });
    }

    private static async Task<CachedRelease?> LoadLatestAsync(AtomUpdateDbContext db, long appId,
        UpdateChannel channel, CancellationToken ct)
    {
        var release = await db.Releases.AsNoTracking()
            .Where(r => r.AppId == appId && r.Status == ReleaseStatus.Published)
            .Where(r => db.ReleaseChannels.Any(rc =>
                rc.ReleaseId == r.Id && rc.Channel == channel &&
                rc.Status == ReleaseChannelStatus.Published))
            .OrderByDescending(r => r.VersionMajor).ThenByDescending(r => r.VersionMinor)
            .ThenByDescending(r => r.VersionPatch).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (release is null)
        {
            return null;
        }

        var assets = await db.ReleaseAssets.AsNoTracking()
            .Where(a => a.ReleaseId == release.Id)
            .OrderBy(a => a.Id)
            .Select(a => new CachedReleaseAsset
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
            })
            .ToListAsync(ct);

        var notes = await db.ReleaseNotes.AsNoTracking()
            .Where(n => n.ReleaseId == release.Id)
            .OrderBy(n => n.Id)
            .Select(n => new CachedReleaseNote
            {
                Language = n.Language,
                New = n.New,
                Improvements = n.Improvements,
                Fixes = n.Fixes,
                Notes = n.Notes
            })
            .ToListAsync(ct);

        return new CachedRelease
        {
            ReleaseId = release.Id,
            Version = release.Version,
            ForceUpdate = release.ForceUpdate,
            MinSupportedVersion = release.MinSupportedVersion,
            Assets = assets,
            Notes = notes
        };
    }

    /// <summary>按客户端架构筛资产。arm64 无对应包时回落 x64（WoA/Rosetta 可转译运行），
    /// 反向不回落——x64 机器跑不了 arm64 包，回落只会把不可用的包发给客户端。
    /// 无任何匹配架构时返回空集，由调用侧按"无更新可发"处理。</summary>
    private static IReadOnlyList<CachedReleaseAsset> ForArch(IReadOnlyList<CachedReleaseAsset>? assets,
        UpdateArch arch)
    {
        if (assets is not { Count: > 0 })
        {
            return [];
        }

        var exact = assets.Where(a => a.Arch == arch).ToList();
        if (exact.Count > 0 || arch != UpdateArch.Arm64)
        {
            return exact;
        }

        return [.. assets.Where(a => a.Arch == UpdateArch.X64)];
    }

    private static CachedReleaseAsset? BestDelta(IReadOnlyList<CachedReleaseAsset> assets, string currentNorm) =>
        assets.Where(a => a.Kind == AssetKind.Delta && a.FromVersion == currentNorm)
            .OrderBy(a => a.PackageType == PackageType.Zip ? 0 : 1).ThenBy(a => a.Id)
            .FirstOrDefault();

    private static CachedReleaseAsset? BestFull(IReadOnlyList<CachedReleaseAsset> assets, bool preferZip) =>
        assets.Where(a => a.Kind == AssetKind.Full)
            .OrderBy(a => (a.PackageType == PackageType.Zip) == preferZip ? 0 : 1).ThenBy(a => a.Id)
            .FirstOrDefault();

    private static PackageAsset ToPackage(CachedReleaseAsset asset, AssetKind? type) => new()
    {
        Type = type,
        PackageType = asset.PackageType,
        Arch = asset.Arch,
        FromVersion = asset.FromVersion,
        Url = asset.Url,
        Size = asset.Size,
        Sha256 = asset.Sha256,
        Signature = asset.Signature,
        KeyId = asset.KeyId,
        SilentArgs = asset.SilentArgs,
        InstallArgs = asset.InstallArgs
    };

    private static PolicyDto ToPolicyDto(PolicyEntity? policy) => policy is null
        ? new PolicyDto
        {
            Version = 0,
            PollingEnabled = true,
            PollingIntervalMinutes = 360,
            PollingJitterMinutes = 10,
            DownloadOnStartup = true,
            DownloadOnManualCheck = true,
            DownloadOnForceUpdate = true,
            InstallMode = InstallMode.OnRestart,
            AllowForceUpdate = false,
            AllowDowngrade = false,
            AllowPostpone = true,
            MaxPostponeCount = 3,
            UpdatedAt = DateTimeOffset.UnixEpoch
        }
        : new PolicyDto
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
            UpdatedAt = policy.UpdatedAt
        };

    /// <summary>ETag = hash16(projectKey|appId|platform|arch|currentVersion|latestVersion|p{policyVersion})，含 latestVersion 以修正"有更新但策略未变仍返回 304"的漏发缺陷（03 §3.1/§4）。</summary>
    private static string ComputeETag(string projectKey, string appId, UpdatePlatform platform, UpdateArch arch,
        string currentVersion, string latestVersion, int policyVersion)
    {
        var raw = string.Create(CultureInfo.InvariantCulture,
            $"{projectKey}|{appId}|{platform.ToString().ToLowerInvariant()}|{arch.ToString().ToLowerInvariant()}|{currentVersion}|{latestVersion}|p{policyVersion}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"\"{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}\"";
    }

    private static bool IsNotModified(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        var expected = etag.Trim('"');
        foreach (var part in ifNoneMatch.Split(','))
        {
            var candidate = part.Trim();
            if (candidate == "*")
            {
                return true;
            }

            if (candidate.StartsWith("W/", StringComparison.Ordinal))
            {
                candidate = candidate[2..].Trim();
            }

            if (candidate.Trim('"').Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new ErrorResponse { Code = code, Message = message }, statusCode: statusCode);

    private static IResult BadRequest(string code, string message) =>
        Error(StatusCodes.Status400BadRequest, code, message);

    private static IResult Unauthorized(string message) =>
        Error(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, message);

    private static IResult NotFound(string code, string message) =>
        Error(StatusCodes.Status404NotFound, code, message);
}
