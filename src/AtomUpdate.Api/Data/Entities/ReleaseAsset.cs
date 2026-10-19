using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

public sealed class ReleaseAsset : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long ReleaseId { get; set; }
    public AssetKind Kind { get; set; }

    /// <summary>delta 起点版本；full 为空。</summary>
    public string? FromVersion { get; set; }

    /// <summary>该包适用的 CPU 架构（平台在应用上，07 Q24）。</summary>
    public UpdateArch Arch { get; set; }

    public PackageType PackageType { get; set; }
    public string Url { get; set; } = null!;
    public long? Size { get; set; }
    public string Sha256 { get; set; } = null!;
    public string? Signature { get; set; }
    public string? KeyId { get; set; }
    public string? SilentArgs { get; set; }
    public string? InstallArgs { get; set; }
    public long? SourceId { get; set; }

    public Release Release { get; set; } = null!;
}
