using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;
using System.Text.Json.Serialization;

namespace AtomUpdate.Core.Models.Manage;

/// <summary>安装实例列表项。</summary>
public sealed class InstallationDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string InstallationId { get; init; }
    public string? MachineFingerprintHash { get; init; }
    public UpdateChannel? Channel { get; init; }
    public string? LastVersion { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }
}

/// <summary>安装汇总：含机器指纹去重口径。</summary>
public sealed class InstallationSummaryDto
{
    /// <summary>安装实例总数（按 installationId）。</summary>
    public required int Total { get; init; }

    /// <summary>机器指纹去重数（distinct MachineFingerprintHash；未上报指纹的实例不计入）。</summary>
    public required int DistinctFingerprints { get; init; }

    /// <summary>未上报指纹的实例数。</summary>
    public required int WithoutFingerprint { get; init; }

    /// <summary>24h 活跃（LastSeenAt 在 24h 内）。</summary>
    public required int Active24h { get; init; }

    /// <summary>7d 活跃。</summary>
    public required int Active7d { get; init; }

    /// <summary>按最近运行版本分布（Top10，按实例数降序）。</summary>
    public required List<VersionCountDto> ByVersion { get; init; }

    /// <summary>按通道分布（仅含已上报通道的实例）。</summary>
    public required List<ChannelCountDto> ByChannel { get; init; }
}

public sealed class VersionCountDto
{
    public required string Version { get; init; }
    public required int Count { get; init; }
}

public sealed class ChannelCountDto
{
    public required UpdateChannel Channel { get; init; }
    public required int Count { get; init; }
}

/// <summary>下载汇总。</summary>
public sealed class DownloadSummaryDto
{
    public required int Downloads24h { get; init; }
    public required int Downloads7d { get; init; }

    /// <summary>7d 成功率（0~1）。</summary>
    public required double SuccessRate7d { get; init; }

    /// <summary>7d 总字节。</summary>
    public required long Bytes7d { get; init; }

    /// <summary>7d 平均时长（毫秒）。</summary>
    public required double AvgDurationMs7d { get; init; }

    /// <summary>7d 增量包占比（0~1；未上报 kind 的不计入分母）。</summary>
    public required double DeltaShare7d { get; init; }
}

/// <summary>检查汇总：304/缓存命中口径。</summary>
public sealed class CheckSummaryDto
{
    public required int Checks24h { get; init; }
    public required int Checks7d { get; init; }

    /// <summary>7d ETag 304 命中率（0~1）。</summary>
    public required double ETagHitRate7d { get; init; }

    /// <summary>7d 策略未变率（policyUnchanged；客户端策略缓存命中口径，0~1）。</summary>
    public required double PolicyUnchangedRate7d { get; init; }
}

/// <summary>检测记录列表项（客户端更新检查逐条快照；每应用仅保留最近 500 条）。</summary>
public sealed class CheckLogDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string AppKey { get; init; }

    /// <summary>应用名（应用已删除时为 null，退化为 AppKey 展示）。</summary>
    public string? AppName { get; init; }

    public UpdatePlatform Platform { get; init; }
    public UpdateArch Arch { get; init; }
    public required string CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public required bool HasUpdate { get; init; }
    public required bool ETagHit { get; init; }
    public string? ClientIp { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
}
