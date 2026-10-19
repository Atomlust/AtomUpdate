using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Packaging;

/// <summary>包文件名（docs/05 §6）：{projectKey}-{appId}-{version}-{platform}-{arch}.{ext}；增量 delta-{from}-to-{to}-{arch}.zip。</summary>
public static class PackageNameBuilder
{
    public static string BuildFull(string projectKey, string appId, string version, UpdatePlatform platform,
        UpdateArch arch, PackageType packageType) =>
        $"{projectKey}-{appId}-{version}-{platform.ToString().ToLowerInvariant()}-{arch.ToString().ToLowerInvariant()}{ExtensionFor(packageType)}";

    public static string BuildFull(PackageContext context) =>
        BuildFull(context.ProjectKey, context.AppId, context.Version, context.Platform, context.Arch,
            context.PackageType);

    public static string BuildDelta(string fromVersion, string toVersion, UpdateArch arch) =>
        $"delta-{fromVersion}-to-{toVersion}-{arch.ToString().ToLowerInvariant()}.zip";

    public static string BuildDelta(DeltaContext context) =>
        BuildDelta(context.FromVersion, context.Version, context.Arch);

    public static string ExtensionFor(PackageType packageType) => packageType switch
    {
        PackageType.Zip => ".zip",
        PackageType.Exe => ".exe",
        PackageType.Msi => ".msi",
        PackageType.Dmg => ".dmg",
        PackageType.Pkg => ".pkg",
        PackageType.AppImage => ".AppImage",
        PackageType.Deb => ".deb",
        PackageType.Rpm => ".rpm",
        _ => throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
            $"不支持的包类型：{packageType}")
    };
}
