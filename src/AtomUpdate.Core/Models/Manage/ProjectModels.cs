using System.Text.Json.Serialization;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;

namespace AtomUpdate.Core.Models.Manage;

public sealed class ProjectDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required EntityStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class ProjectCreateRequest
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public sealed class ProjectUpdateRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public EntityStatus? Status { get; init; }
}
