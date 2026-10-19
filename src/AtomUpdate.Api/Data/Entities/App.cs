using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

public sealed class App : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long ProjectId { get; set; }

    /// <summary>应用业务标识，如 "com.company.myapp"。</summary>
    public string AppId { get; set; } = null!;

    public string Name { get; set; } = null!;
    public AppType Type { get; set; } = AppType.App;

    /// <summary>应用所属操作系统（一个 appId 只服务一个平台，07 Q24）；创建后不可改。</summary>
    public UpdatePlatform Platform { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
