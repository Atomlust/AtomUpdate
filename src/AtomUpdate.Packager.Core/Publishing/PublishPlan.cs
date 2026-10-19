using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Publishing;

/// <summary>单架构打包目标（docs/05 §16 publish 编排）：来源目录 + 包类型；BaselineDirectory 非空则同时产增量。</summary>
public sealed class PackageTarget
{
    public required UpdateArch Arch { get; init; }
    public required PackageType PackageType { get; init; }
    public required string SourceDirectory { get; init; }

    /// <summary>增量基线目录；非空时产出 delta-{fromVersion}-to-{version}。</summary>
    public string? BaselineDirectory { get; init; }

    /// <summary>exe/msi 静默/安装参数。</summary>
    public string? SilentArgs { get; init; }
    public string? InstallArgs { get; init; }
}

/// <summary>发布全链路计划（CLI 与 Avalonia 向导共用，docs/05 §12/§16）：
/// 打包（每架构 full，可选 delta）→ 上传（服务端）→ 合并单请求推送 → 可选发布。</summary>
public sealed class PublishPlan
{
    // 命名/布局
    public required string ProjectKey { get; init; }
    public required string AppId { get; init; }
    public required string Version { get; init; }
    public required UpdatePlatform Platform { get; init; }
    public required IReadOnlyList<PackageTarget> Targets { get; init; }
    public string? OutputDirectory { get; init; }
    public IReadOnlyList<string> ExcludePatterns { get; init; } = PackageContext.DefaultExcludes;
    public string? InnoSetupPath { get; init; }

    // 增量起点（任一目标带基线时必填）
    public string? FromVersion { get; init; }

    // Api（数字主键，上传与推送均走 JWT 登录会话）
    public required long ProjectId { get; init; }
    public required long AppNumericId { get; init; }

    // 元数据
    public int? BuildNumber { get; init; }
    public ReleaseStatus? Status { get; init; }
    public bool ForceUpdate { get; init; }
    public string? MinSupportedVersion { get; init; }
    public ReleaseNoteInput? Notes { get; init; }

    /// <summary>推送成功后立即发布到 Channels。</summary>
    public bool PublishAfterCreate { get; init; }
    public IReadOnlyList<UpdateChannel> Channels { get; init; } = [];
}

/// <summary>发布结果：最终 ReleaseDto + 各阶段产物（CLI/GUI 汇总展示）。</summary>
public sealed record PublishResult(
    ReleaseDto Release,
    IReadOnlyList<PackageResult> Packages,
    IReadOnlyList<DeltaResult> Deltas,
    IReadOnlyList<UploadResult> Uploads,
    bool Published);
