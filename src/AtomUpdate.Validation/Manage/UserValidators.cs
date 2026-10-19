using AtomUpdate.Core.Models.Manage;
using FluentValidation;

namespace AtomUpdate.Validation.Manage;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // 登录标识：用户名或邮箱（用户名不是邮箱格式，故不校验 EmailAddress）
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public sealed class UserCreateRequestValidator : AbstractValidator<UserCreateRequest>
{
    public UserCreateRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(200);
        // 用户名：可选；填写时 3-64 位、不含空白
        RuleFor(x => x.UserName).MinimumLength(3).MaximumLength(64)
            .Must(name => name!.All(char.IsWhiteSpace) is false)
            .When(x => !string.IsNullOrWhiteSpace(x.UserName));
        RuleFor(x => x.DisplayName).MaximumLength(100);
    }
}

public sealed class UserUpdateRequestValidator : AbstractValidator<UserUpdateRequest>
{
    public UserUpdateRequestValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Password).MinimumLength(8).MaximumLength(200)
            .When(x => x.Password is not null);
        // 用户名：null=不改；非空（含空串清除）时限制长度
        RuleFor(x => x.UserName).MaximumLength(64)
            .Must(name => name!.Trim().Length == 0 || name.Trim().Length >= 3)
            .When(x => x.UserName is not null);
    }
}

/// <summary>首次部署设置向导：规则与用户创建一致（邮箱格式 + 密码强度 + 可选用户名）。</summary>
public sealed class SetupRequestValidator : AbstractValidator<SetupRequest>
{
    public SetupRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(200);
        RuleFor(x => x.UserName).MinimumLength(3).MaximumLength(64)
            .Must(name => name!.All(char.IsWhiteSpace) is false)
            .When(x => !string.IsNullOrWhiteSpace(x.UserName));
        RuleFor(x => x.DisplayName).MaximumLength(100);
    }
}
