using AtomUpdate.Core.Models.Manage;
using FluentValidation;

namespace AtomUpdate.Validation.Manage;

public sealed class AppCreateRequestValidator : AbstractValidator<AppCreateRequest>
{
    public AppCreateRequestValidator()
    {
        RuleFor(x => x.AppId).NotEmpty().MaximumLength(200).Matches(ValidationPatterns.AppBusinessId)
            .WithMessage("appId 只能包含字母、数字、点、下划线和连字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Platform).NotNull().WithMessage("platform 必填（一个应用只服务一个平台）")
            .IsInEnum().WithMessage("platform 取值非法");
    }
}

public sealed class AppUpdateRequestValidator : AbstractValidator<AppUpdateRequest>
{
    public AppUpdateRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}
