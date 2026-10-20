using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>下载日志（03 §2.4，按 DownloadReport 契约收敛：无 ReleaseAssetId/Mode）。
/// 后台批量写；按月分区留待规模期。</summary>
public sealed class DownloadLog : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long AppId { get; set; }
    public string InstallationId { get; set; } = null!;
    public string FromVersion { get; set; } = null!;
    public string ToVersion { get; set; } = null!;

    /// <summary>包类型（full/delta；SDK 上报，旧客户端为空）。</summary>
    public AssetKind? Kind { get; set; }

    public bool Success { get; set; }
    public long Bytes { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset DownloadedAt { get; set; }
}
