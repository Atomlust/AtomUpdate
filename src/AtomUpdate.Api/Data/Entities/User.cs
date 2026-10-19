using AtomUpdate.Core.Enums;

namespace AtomUpdate.Api.Data.Entities;

public sealed class User : ITenantScoped
{
    public long Id { get; set; }
    public long TenantId { get; set; }

    /// <summary>全局唯一（M2 决策，见 docs/07 §4.2），登录仅"邮箱+密码"两要素。</summary>
    public string Email { get; set; } = null!;

    /// <summary>可选登录名（全局唯一，可空）：设置后可用"用户名+密码"登录；存储规范化为小写。</summary>
    public string? UserName { get; set; }

    /// <summary>PasswordHasher 存储格式：v1.{iterations}.{salt-b64}.{hash-b64}。</summary>
    public string PasswordHash { get; set; } = null!;

    public string? DisplayName { get; set; }

    /// <summary>平台管理员：可管理租户（MVP 两级权限模型，docs/07 §4.2）。</summary>
    public bool IsPlatformAdmin { get; set; }

    public EntityStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
