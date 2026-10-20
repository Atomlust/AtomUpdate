using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>版本（平台由所属应用决定、架构在各资产上）。VersionMajor/Minor/Patch 为解析冗余，供 SQL 排序。</summary>
public sealed class Release : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long AppId { get; set; }
    public string Version { get; set; } = null!;
    public int VersionMajor { get; set; }
    public int VersionMinor { get; set; }
    public int VersionPatch { get; set; }
    public int? BuildNumber { get; set; }
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Draft;
    public bool ForceUpdate { get; set; }
    public string? MinSupportedVersion { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
