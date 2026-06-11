using FluentValidation;
using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Ui.Validation;

public class ProfileValidator : MudCompatibleValidator<Profile>
{
    public ProfileValidator()
    {
        RuleFor(x => x.GoalsText)
            .MaximumLength(4000).WithMessage("Goals are too long (max 4000 characters).");

        RuleFor(x => x.DriftPctThreshold)
            .InclusiveBetween(0, 100).WithMessage("Drift threshold must be between 0 and 100%.");

        RuleFor(x => x.SingleDayMovePctThreshold)
            .InclusiveBetween(0, 100).WithMessage("Move threshold must be between 0 and 100%.");

        RuleFor(x => x.RebalanceCadenceHours)
            .GreaterThanOrEqualTo(1).WithMessage("Cadence must be at least 1 hour.");

        RuleFor(x => x.SystemPromptOverride)
            .MaximumLength(20000).WithMessage("Prompt override is too long (max 20000 characters).");
    }
}
