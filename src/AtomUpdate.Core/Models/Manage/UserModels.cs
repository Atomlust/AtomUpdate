using System.Text.Json.Serialization;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Serialization;

namespace AtomUpdate.Core.Models.Manage;

public sealed class LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed class LoginResponse
{
    public required string Token { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required UserDto User { get; init; }
}

public sealed class UserDto
{
    [JsonConverter(typeof(LongIdJsonConverter))]
    public required long Id { get; init; }

    public required string Email { get; init; }

    /// <summary>可选登录名；设置后可用"用户名+密码"登录。</summary>
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }
    public required bool IsPlatformAdmin { get; init; }
    public required EntityStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>引导创建的超级管理员（Auth:Bootstrap:Email）：后端禁止删除，前端隐藏删除入口。</summary>
    public bool IsBootstrapAdmin { get; init; }
}

public sealed class UserCreateRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }

    /// <summary>可选登录名（3-64 位，存储前规范化为小写）；留空则仅可用邮箱登录。</summary>
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>仅平台管理员调用方生效；其余调用方一律创建为普通租户用户。</summary>
    public bool? IsPlatformAdmin { get; init; }
}

public sealed class UserUpdateRequest
{
    public string? DisplayName { get; init; }
    public EntityStatus? Status { get; init; }
    public string? Password { get; init; }

    /// <summary>可选登录名；null=不修改，空串=清除（此后仅邮箱登录），非空=设置/修改。</summary>
    public string? UserName { get; init; }
    public bool? IsPlatformAdmin { get; init; }
}

public sealed class ChangePasswordRequest
{
    public required string OldPassword { get; init; }
    public required string NewPassword { get; init; }
}

/// <summary>初始化向导状态（GET /api/manage/auth/setup/status，公开）。</summary>
public sealed class SetupStatusResponse
{
    /// <summary>true = 用户表为空，首次部署需先完成管理员初始化。</summary>
    public bool Required { get; init; }
}

/// <summary>首次部署设置向导（POST /api/manage/auth/setup，公开）：用户表为空时创建默认租户 +
/// 平台管理员并直接签发登录会话；表非空一律 409。Auth:Bootstrap 预配路径（CI/自动化）之外的首选初始化方式。</summary>
public sealed class SetupRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }

    /// <summary>可选登录名（3-64 位，存储前规范化为小写）；留空默认 admin。</summary>
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }
}
