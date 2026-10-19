namespace AtomUpdate.Api.Data.Entities;

/// <summary>多租户隔离标记：EF Global Query Filter 依据 TenantContext.TenantId 强制过滤。</summary>
public interface ITenantScoped
{
    long TenantId { get; set; }
}
