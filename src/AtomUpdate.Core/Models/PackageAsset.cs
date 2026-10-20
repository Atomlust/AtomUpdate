using AtomUpdate.Core.Enums;

namespace AtomUpdate.Core.Models;

public sealed class PackageAsset
{
    public AssetKind? Type { get; init; }
    public required PackageType PackageType { get; init; }

    /// <summary>包目标架构：验签内容含架构，arm64 设备回落 x64 包时必须用包架构而非设备架构。</summary>
    public required UpdateArch Arch { get; init; }
    public string? FromVersion { get; init; }
    public required string Url { get; init; }
    public long? Size { get; init; }
    public required string Sha256 { get; init; }
    public string? Signature { get; init; }
    public string? KeyId { get; init; }
    public string? SilentArgs { get; init; }
    public string? InstallArgs { get; init; }
}
