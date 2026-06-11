using FluentValidation;
using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Ui.Validation;

public class HoldingValidator : MudCompatibleValidator<Holding>
{
    public HoldingValidator()
    {
        RuleFor(x => x.Ticker).ValidTicker();

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Name is too long (max 200 characters).");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Pick a currency.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0.");

        RuleFor(x => x.AvgCost)
            .GreaterThanOrEqualTo(0).WithMessage("Average cost can't be negative.");

        RuleFor(x => x.TargetAllocationPct)
            .InclusiveBetween(0, 100)
            .When(x => x.TargetAllocationPct.HasValue)
            .WithMessage("Target allocation must be between 0 and 100%.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000).WithMessage("Notes are too long (max 2000 characters).");
    }
}
