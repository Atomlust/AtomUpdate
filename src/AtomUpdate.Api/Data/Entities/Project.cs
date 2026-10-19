using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

public sealed class Project : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
