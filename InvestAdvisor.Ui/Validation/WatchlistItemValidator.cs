using FluentValidation;
using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Ui.Validation;

public class WatchlistItemValidator : MudCompatibleValidator<WatchlistItem>
{
    public WatchlistItemValidator()
    {
        RuleFor(x => x.Ticker).ValidTicker();

        RuleFor(x => x.Note)
            .MaximumLength(2000).WithMessage("Note is too long (max 2000 characters).");

        RuleFor(x => x.PriceTargetLow)
            .GreaterThan(0)
            .When(x => x.PriceTargetLow.HasValue)
            .WithMessage("Price target must be greater than 0.");

        RuleFor(x => x.PriceTargetHigh)
            .GreaterThan(0)
            .When(x => x.PriceTargetHigh.HasValue, ApplyConditionTo.CurrentValidator)
            .WithMessage("Price target must be greater than 0.")
            .GreaterThanOrEqualTo(x => x.PriceTargetLow)
            .When(x => x.PriceTargetLow.HasValue && x.PriceTargetHigh.HasValue, ApplyConditionTo.CurrentValidator)
            .WithMessage("High target must be at or above the low target.");
    }
}
