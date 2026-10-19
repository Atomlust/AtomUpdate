using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

public sealed class Tenant
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
