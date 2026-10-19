using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>版本 × 通道 的发布关系（M2.5：环境已移除）。</summary>
public sealed class ReleaseChannel
{
    public long ReleaseId { get; set; }
    public UpdateChannel Channel { get; set; }
    public ReleaseChannelStatus Status { get; set; } = ReleaseChannelStatus.Published;
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>发布序号：bump 即缓存失效（02 §3.1）。</summary>
    public int ReleaseSeq { get; set; }

    public Release Release { get; set; } = null!;
}
