using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>安装实例（03 §2.4）：按 (AppId, InstallationId) 去重，任一上报到达即 upsert 刷新活跃。</summary>
public sealed class Installation : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long AppId { get; set; }
    public App App { get; set; } = null!;

    /// <summary>客户端生成的安装标识（GUID）。</summary>
    public string InstallationId { get; set; } = null!;

    /// <summary>机器指纹哈希（契约约定客户端上报 sha256 摘要，服务端原样存储）。</summary>
    public string? MachineFingerprintHash { get; set; }

    public UpdateChannel? Channel { get; set; }

    /// <summary>最近上报的运行版本（startup/install 上报刷新，供版本分布统计）。</summary>
    public string? LastVersion { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
