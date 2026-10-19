using System.Text.Json.Serialization;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;

namespace AtomUpdate.Core.Models.Manage;

public sealed class ReleaseCreateRequest
{
    public required string Version { get; init; }
    public int? BuildNumber { get; init; }

    /// <summary>仅允许 draft / prerelease；发布走 publish 端点。</summary>
    public ReleaseStatus? Status { get; init; }

    public bool ForceUpdate { get; init; }
    public string? MinSupportedVersion { get; init; }
    public required IReadOnlyList<ReleaseAssetInput> Assets { get; init; }
    public ReleaseNoteInput? Notes { get; init; }
}

public sealed class ReleaseAssetInput
{
    public required AssetKind Kind { get; init; }

    /// <summary>delta 必填（增量起点版本），full 必须为空。</summary>
    public string? FromVersion { get; init; }

    /// <summary>该包适用的 CPU 架构；平台由应用决定（07 Q24，一条版本可挂多架构资产）。</summary>
    public required UpdateArch Arch { get; init; }

    public required PackageType PackageType { get; init; }
    public required string Url { get; init; }
    public long? Size { get; init; }
    public required string Sha256 { get; init; }
    public string? Signature { get; init; }
    public string? KeyId { get; init; }
    public string? SilentArgs { get; init; }
    public string? InstallArgs { get; init; }
    public long? SourceId { get; init; }
}

public sealed class ReleaseNoteInput
{
    public string Language { get; init; } = "en";
    public IReadOnlyList<string> New { get; init; } = [];
    public IReadOnlyList<string> Improvements { get; init; } = [];
    public IReadOnlyList<string> Fixes { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    public string? RawMarkdown { get; init; }
}

public sealed class ReleaseAssetDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
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

public sealed class ReleaseNoteDto
{
    public required string Language { get; init; }
    public required IReadOnlyList<string> New { get; init; }
    public required IReadOnlyList<string> Improvements { get; init; }
    public required IReadOnlyList<string> Fixes { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public string? RawMarkdown { get; init; }
}

public sealed class ReleaseChannelDto
{
    public required UpdateChannel Channel { get; init; }
    public required ReleaseChannelStatus Status { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public required int ReleaseSeq { get; init; }
}

public sealed class ReleaseDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string Version { get; init; }
    public int? BuildNumber { get; init; }
    public required ReleaseStatus Status { get; init; }
    public required bool ForceUpdate { get; init; }
    public string? MinSupportedVersion { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<ReleaseAssetDto> Assets { get; init; }
    public ReleaseNoteDto? Notes { get; init; }
    public required IReadOnlyList<ReleaseChannelDto> Channels { get; init; }
}

/// <summary>管理端跨应用版本列表行（版本页默认全量展示 + 项目/应用检索，docs/03 §6.2 管理面）。</summary>
public sealed class ManageReleaseDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string Version { get; init; }
    public int? BuildNumber { get; init; }
    public required ReleaseStatus Status { get; init; }
    public required bool ForceUpdate { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<ReleaseAssetDto> Assets { get; init; }

    public required long ProjectId { get; init; }
    public required long AppId { get; init; }
    public required string ProjectName { get; init; }
    public required string AppName { get; init; }
    public required string AppBusinessId { get; init; }
    public required string Platform { get; init; }
}

public sealed class ReleasePublishRequest
{
    public required IReadOnlyList<UpdateChannel> Channels { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>草稿编辑（仅 draft/prerelease 可改；docs/07 §4.2 M2-D）。null 字段不更新。</summary>
public sealed class ReleaseUpdateRequest
{
    public int? BuildNumber { get; init; }
    public bool? ForceUpdate { get; init; }
    public string? MinSupportedVersion { get; init; }
    public ReleaseNoteInput? Notes { get; init; }
}

public sealed class AssetUploadResult
{
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
    public required string Filename { get; init; }
}
