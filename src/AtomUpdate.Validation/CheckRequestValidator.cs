using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using FluentValidation;
using NuGet.Versioning;

namespace AtomUpdate.Validation;

public sealed class CheckRequestValidator : AbstractValidator<CheckRequest>
{
    public CheckRequestValidator()
    {
        RuleFor(x => x.ProjectKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AppId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CurrentVersion)
            .NotEmpty()
            .Must(BeSemanticVersion)
            .WithErrorCode(ErrorCodes.InvalidVersion)
            .WithMessage("currentVersion 必须是合法的语义化版本号");
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.Arch).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum().When(x => x.Channel is not null);
        RuleFor(x => x.InstallationId)
            .NotEmpty()
            .Must(BeGuid)
            .WithMessage("installationId 必须是合法的 GUID");
        RuleFor(x => x.MachineFingerprint).MaximumLength(200);
        RuleFor(x => x.PolicyVersion).GreaterThanOrEqualTo(0);
    }

    private static bool BeSemanticVersion(string version) => SemanticVersion.TryParse(version, out _);

    private static bool BeGuid(string installationId) => Guid.TryParse(installationId, out _);
}
