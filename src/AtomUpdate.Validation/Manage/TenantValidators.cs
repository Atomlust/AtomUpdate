using AtomUpdate.Core.Models.Manage;
using FluentValidation;

namespace AtomUpdate.Validation.Manage;

public sealed class TenantCreateRequestValidator : AbstractValidator<TenantCreateRequest>
{
    public TenantCreateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100).Matches(ValidationPatterns.Slug)
            .WithMessage("slug 只能包含小写字母、数字和连字符");
    }
}

public sealed class TenantUpdateRequestValidator : AbstractValidator<TenantUpdateRequest>
{
    public TenantUpdateRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Status).IsInEnum();
    }
}
