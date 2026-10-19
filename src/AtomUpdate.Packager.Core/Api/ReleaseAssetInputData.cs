using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Api;

/// <summary>资产元数据输入（Packager 内部编排模型）：打包结果 + 上传结果 → ReleaseAssetInput。</summary>
public sealed class ReleaseAssetInputData
{
    public required AssetKind Kind { get; init; }

    /// <summary>delta 必填（增量起点版本）；full 必须为空。</summary>
    public string? FromVersion { get; init; }

    public required UpdateArch Arch { get; init; }
    public required PackageType PackageType { get; init; }
    public required string Url { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public string? SilentArgs { get; init; }
    public string? InstallArgs { get; init; }

    internal ReleaseAssetInput ToContract() => new()
    {
        Kind = Kind,
        FromVersion = FromVersion,
        Arch = Arch,
        PackageType = PackageType,
        Url = Url,
        Size = Size,
        Sha256 = Sha256,
        SilentArgs = SilentArgs,
        InstallArgs = InstallArgs
    };

    public static ReleaseAssetInputData Full(PackageResult package, UploadResult upload) =>
        new()
        {
            Kind = AssetKind.Full,
            Arch = package.Arch,
            PackageType = package.PackageType,
            Url = upload.Url,
            Size = upload.Size,
            Sha256 = package.Sha256,
            SilentArgs = package.SilentArgs,
            InstallArgs = package.InstallArgs
        };

    public static ReleaseAssetInputData Delta(DeltaResult delta, UploadResult upload) =>
        new()
        {
            Kind = AssetKind.Delta,
            FromVersion = delta.FromVersion,
            Arch = delta.Arch,
            PackageType = PackageType.Zip,
            Url = upload.Url,
            Size = upload.Size,
            Sha256 = delta.Sha256
        };
}
