using AtomUpdate.Core.Serialization;
using System.Text.Json.Serialization;

namespace AtomUpdate.Core.Models.Manage;

/// <summary>租户概览（M2 W4 轻量版，深化归 M6）。</summary>
public sealed class OverviewDto
{
    public required int Projects { get; init; }
    public required int Apps { get; init; }
    public required int PublishedReleases { get; init; }

    public required int Checks24h { get; init; }

    /// <summary>24h 内去重安装数（installationId）。</summary>
    public required int ActiveInstalls24h { get; init; }

    /// <summary>24h ETag 命中率（0~1）。</summary>
    public required double ETagHitRate24h { get; init; }

    public required int Users { get; init; }
}

public sealed class AuditLogDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public string? Actor { get; init; }

    /// <summary>操作人显示名（DisplayName → UserName → Email 兜底解析；用户已删除时回退原始标识）。</summary>
    public string? ActorName { get; init; }

    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? Details { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
