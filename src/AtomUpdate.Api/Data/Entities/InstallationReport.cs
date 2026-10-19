using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

/// <summary>安装结果上报（03 §2.4/§4）：更新器直传 + SDK 补报共用；
/// 幂等唯一索引 (AppId, InstallationId, ToVersion, Stage, ErrorCode) 去重（直传与补报可能重复）。</summary>
public sealed class InstallationReport : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long AppId { get; set; }
    public string InstallationId { get; set; } = null!;
    public string? FromVersion { get; set; }
    public string ToVersion { get; set; } = null!;
    public UpdateStage Stage { get; set; }
    public InstallMode InstallMode { get; set; }
    public bool Elevated { get; set; }
    public bool ElevationRejected { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
}
