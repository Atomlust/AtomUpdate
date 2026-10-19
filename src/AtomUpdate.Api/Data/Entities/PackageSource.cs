namespace AtomUpdate.Api.Data.Entities;

/// <summary>资产来源（存储标识、区域），不含凭据。</summary>
public sealed class PackageSource : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long AppId { get; set; }
    public string Provider { get; set; } = null!;
    public string? Region { get; set; }
    public string? Bucket { get; set; }
    public string DisplayName { get; set; } = null!;
}
