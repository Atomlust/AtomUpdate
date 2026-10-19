using System.Text.Json.Serialization;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;

namespace AtomUpdate.Core.Models.Manage;

public sealed class TenantDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required EntityStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class TenantCreateRequest
{
    public required string Name { get; init; }
    public required string Slug { get; init; }
}

public sealed class TenantUpdateRequest
{
    public string? Name { get; init; }
    public EntityStatus? Status { get; init; }
}
