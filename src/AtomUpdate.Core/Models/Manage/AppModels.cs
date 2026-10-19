using System.Text.Json.Serialization;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;

namespace AtomUpdate.Core.Models.Manage;

public sealed class AppDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string AppId { get; init; }
    public required string Name { get; init; }
    public required AppType Type { get; init; }

    /// <summary>应用运行的操作系统；版本包的平台维度由此决定，创建后不可改（07 Q24）。</summary>
    public required UpdatePlatform Platform { get; init; }

    public required EntityStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>跨项目列表（/api/manage/apps）用；单项目上下文可为空。</summary>
    [JsonConverter(typeof(LongIdJsonConverter))]
    public long ProjectId { get; init; }

    public string? ProjectName { get; init; }
}

public sealed class AppCreateRequest
{
    public required string AppId { get; init; }
    public required string Name { get; init; }
    public AppType? Type { get; init; }
    public UpdatePlatform? Platform { get; init; }
}

public sealed class AppUpdateRequest
{
    public string? Name { get; init; }
    public AppType? Type { get; init; }
    public EntityStatus? Status { get; init; }
}
