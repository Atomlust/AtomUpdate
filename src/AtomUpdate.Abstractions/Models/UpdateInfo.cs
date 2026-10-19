using AtomUpdate.Core.Models;

namespace AtomUpdate.Abstractions.Models;

/// <summary>当前可用更新（门面 CurrentUpdate 与下载/安装上下文共享）。</summary>
public sealed class UpdateInfo
{
    /// <summary>最新版本号。</summary>
    public required string LatestVersion { get; init; }

    /// <summary>是否强制更新（服务端 forceUpdate 或 current &lt; minSupportedVersion）。</summary>
    public bool ForceUpdate { get; init; }

    /// <summary>最低支持版本（低于即强制）。</summary>
    public string? MinSupportedVersion { get; init; }

    /// <summary>优先使用的包（增量包优先，可能为空 → 用 FullPackage）。</summary>
    public PackageAsset? Package { get; init; }

    /// <summary>全量包（兜底）。</summary>
    public PackageAsset? FullPackage { get; init; }

    public ReleaseNotes? ReleaseNotes { get; init; }

    /// <summary>获取实际应下载的包（增量优先，回退全量）。</summary>
    public PackageAsset? ResolvePackage() => Package ?? FullPackage;
}
