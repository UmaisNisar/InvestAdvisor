using System.Text.RegularExpressions;
using FluentValidation;

namespace InvestAdvisor.Ui.Validation;

public static partial class TickerRules
{
    // Letters/digits plus dots and hyphens for exchange suffixes and share classes
    // (e.g. SHOP.TO, BTCC.B.TO, BRK-B). Surrounding whitespace is trimmed on save.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{0,15}$")]
    private static partial Regex Pattern();

    public static IRuleBuilderOptions<T, string> ValidTicker<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Ticker is required.")
            .Must(t => Pattern().IsMatch(t.Trim()))
            .WithMessage("Use letters, digits, dots or hyphens (max 16 characters), e.g. SHOP.TO.");
}
