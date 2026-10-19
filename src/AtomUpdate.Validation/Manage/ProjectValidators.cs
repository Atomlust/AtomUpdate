using AtomUpdate.Core.Models.Manage;
using FluentValidation;

namespace AtomUpdate.Validation.Manage;

public sealed class ProjectCreateRequestValidator : AbstractValidator<ProjectCreateRequest>
{
    public ProjectCreateRequestValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100).Matches(ValidationPatterns.Slug)
            .WithMessage("key 只能包含小写字母、数字和连字符");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class ProjectUpdateRequestValidator : AbstractValidator<ProjectUpdateRequest>
{
    public ProjectUpdateRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Status).IsInEnum();
    }
}
