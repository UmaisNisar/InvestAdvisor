using FluentValidation;
using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Core.Validation;

public class RealizedLotValidator : AbstractValidator<RealizedLot>
{
    public RealizedLotValidator()
    {
        RuleFor(x => x.Ticker).ValidTicker();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name is too long (max 200 characters).");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Pick a currency.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0.");

        RuleFor(x => x.Proceeds)
            .GreaterThanOrEqualTo(0).WithMessage("Proceeds can't be negative.");

        RuleFor(x => x.CostBasis)
            .GreaterThanOrEqualTo(0).WithMessage("Cost basis can't be negative.");
    }
}
