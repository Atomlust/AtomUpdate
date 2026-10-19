using AtomUpdate.Core.Models.Manage;
using FluentValidation;

namespace AtomUpdate.Validation.Manage;

public sealed class PolicyUpdateRequestValidator : AbstractValidator<PolicyUpdateRequest>
{
    public PolicyUpdateRequestValidator()
    {
        RuleFor(x => x.PollingIntervalMinutes).InclusiveBetween(1, 10080);
        RuleFor(x => x.PollingJitterMinutes).InclusiveBetween(0, 1440);
        RuleFor(x => x.InstallMode).IsInEnum();
        RuleFor(x => x.MaxPostponeCount).InclusiveBetween(0, 100);
        RuleFor(x => x.MaxDownloadSpeedKBps).InclusiveBetween(0, 1048576);
    }
}
