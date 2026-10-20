using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Cache;

/// <summary>
/// 读路径发布缓存（02 §3.1）：按 (app, channel) 缓存"最新已发布版本"——平台在应用上、架构在各资产上，
/// 所以缓存条目与请求架构无关，选包时再按客户端上报的 arch 命中资产；
/// 发布/撤回 bump 应用级 epoch 使该应用全部条目失效；无 Redis 时退回内存 + TTL 30s。
/// </summary>
public interface IReleaseCache
{
    /// <summary>取最新已发布版本；未命中时经 <paramref name="load"/> 回源并写入缓存（含空结果）。</summary>
    Task<CachedRelease?> GetLatestAsync(ReleaseCacheKey key,
        Func<CancellationToken, Task<CachedRelease?>> load, CancellationToken ct);

    /// <summary>应用维度失效：发布/撤回后调用，下个读请求即回源。</summary>
    void Invalidate(long appId);
}

public readonly record struct ReleaseCacheKey(long AppId, UpdateChannel Channel);

public sealed record CachedRelease
{
    public required long ReleaseId { get; init; }
    public required string Version { get; init; }
    public required bool ForceUpdate { get; init; }
    public string? MinSupportedVersion { get; init; }
    public required IReadOnlyList<CachedReleaseAsset> Assets { get; init; }

    /// <summary>发行说明随版本同缓存（按 Id 序）：已发布版本不可编辑，命中后 /api/check 不再查说明表。</summary>
    public required IReadOnlyList<CachedReleaseNote> Notes { get; init; }
}

public sealed record CachedReleaseNote
{
    public required string Language { get; init; }
    public required IReadOnlyList<string> New { get; init; }
    public required IReadOnlyList<string> Improvements { get; init; }
    public required IReadOnlyList<string> Fixes { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed record CachedReleaseAsset
{
    public required long Id { get; init; }
    public required AssetKind Kind { get; init; }
    public string? FromVersion { get; init; }
    public required UpdateArch Arch { get; init; }
    public required PackageType PackageType { get; init; }
    public required string Url { get; init; }
    public long? Size { get; init; }
    public required string Sha256 { get; init; }
    public string? Signature { get; init; }
    public string? KeyId { get; init; }
    public string? SilentArgs { get; init; }
    public string? InstallArgs { get; init; }
}
