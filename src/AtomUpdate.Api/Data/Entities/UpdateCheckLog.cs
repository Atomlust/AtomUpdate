using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>检查日志（后台批量写；按月分区留待规模期，M1 普通表）。</summary>
public sealed class UpdateCheckLog : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string InstallationId { get; set; } = null!;
    public long AppId { get; set; }
    public string ProjectKey { get; set; } = null!;
    public string AppKey { get; set; } = null!;
    public UpdatePlatform Platform { get; set; }
    public UpdateArch Arch { get; set; }
    public UpdateChannel? Channel { get; set; }
    public string CurrentVersion { get; set; } = null!;
    public string? LatestVersion { get; set; }
    public bool HasUpdate { get; set; }
    public bool ETagHit { get; set; }
    public bool PolicyUnchanged { get; set; }

    /// <summary>客户端 IP（ForwardedHeaders 还原真实地址；仅记录用途，不做安全决策）。</summary>
    public string? ClientIp { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}
