namespace AtomUpdate.Api.Data.Entities;

public sealed class AuditLog : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }

    /// <summary>操作者（JWT sub）。</summary>
    public string? Actor { get; set; }

    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
