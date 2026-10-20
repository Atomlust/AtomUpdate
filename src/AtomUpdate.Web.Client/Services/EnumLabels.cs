using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomBlazor;

namespace AtomUpdate.Web.Client.Services;

/// <summary>枚举的展示文案：平台来自应用、架构来自包，多页共用。</summary>
public static class EnumLabels
{
    public static string Platform(UpdatePlatform platform) => platform switch
    {
        UpdatePlatform.Macos => "macOS",
        UpdatePlatform.Linux => "Linux",
        UpdatePlatform.Ios => "iOS",
        UpdatePlatform.Android => "Android",
        UpdatePlatform.HarmonyOS => "鸿蒙",
        _ => "Windows"
    };

    public static string Arch(UpdateArch arch) => arch switch
    {
        UpdateArch.Arm64 => "arm64",
        UpdateArch.X86 => "x86",
        _ => "x64"
    };

    /// <summary>一条版本的包架构概览（去重后的顿号列表）。</summary>
    public static string Archs(IEnumerable<ReleaseAssetDto> assets)
    {
        var labels = assets.Select(a => Arch(a.Arch)).Distinct().ToList();
        return labels.Count == 0 ? "—" : string.Join("、", labels);
    }

    /// <summary>版本状态徽标：发布成功色、草稿信息色、预发警示色、撤回/回滚危险色。</summary>
    public static BadgeVariant StatusVariant(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Published => BadgeVariant.SoftSuccess,
        ReleaseStatus.Draft => BadgeVariant.SoftInfo,
        ReleaseStatus.Prerelease => BadgeVariant.SoftWarning,
        _ => BadgeVariant.SoftDestructive
    };
}
