using FluentValidation;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Ui.Validation;

public class RuntimeSettingsValidator : MudCompatibleValidator<RuntimeSettings>
{
    public RuntimeSettingsValidator()
    {
        // Scheduler & triggers
        RuleFor(x => x.DailyBudgetUsd)
            .GreaterThanOrEqualTo(0).WithMessage("Budget can't be negative (0 = unlimited).");

        RuleFor(x => x.TickIntervalSeconds)
            .GreaterThanOrEqualTo(15).WithMessage("Tick interval must be at least 15 seconds.");

        RuleFor(x => x.TimeZoneId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Time zone is required.")
            .Must(BeAKnownTimeZone).WithMessage("Unknown time zone — use an IANA id like America/New_York.");

        RuleFor(x => x.MaxRunsPerDay)
            .GreaterThanOrEqualTo(1).WithMessage("Allow at least 1 run per day.");

        RuleFor(x => x.MinSecondsBetweenRuns)
            .GreaterThanOrEqualTo(0).WithMessage("Gap between runs can't be negative.");

        RuleFor(x => x.MaxSnapshotAgeForTriggerSeconds)
            .GreaterThanOrEqualTo(60).WithMessage("Snapshot age must be at least 60 seconds.");

        RuleFor(x => x.MinPriceFreshnessSeconds)
            .GreaterThanOrEqualTo(0).WithMessage("Price freshness can't be negative.");

        // AI provider
        RuleFor(x => x.LlmModel)
            .NotEmpty().WithMessage("Primary model is required.");

        RuleFor(x => x.LlmRoutineModel)
            .NotEmpty().WithMessage("Routine model is required.");

        RuleFor(x => x.LlmCustomBaseUrl)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .When(x => x.LlmProvider == LlmProviders.Custom, ApplyConditionTo.CurrentValidator)
            .WithMessage("Base URL is required for a custom provider.")
            .Must(BeAValidHttpUrl!)
            .When(x => !string.IsNullOrWhiteSpace(x.LlmCustomBaseUrl), ApplyConditionTo.CurrentValidator)
            .WithMessage("Enter a valid http(s) URL, e.g. https://api.groq.com/openai/v1/.");

        // Screener weights (relative; the scorer normalizes by their sum)
        RuleFor(x => x.WeightGrowth).InclusiveBetween(0, 100).WithMessage(WeightMessage);
        RuleFor(x => x.WeightValuation).InclusiveBetween(0, 100).WithMessage(WeightMessage);
        RuleFor(x => x.WeightAnalyst).InclusiveBetween(0, 100).WithMessage(WeightMessage);
        RuleFor(x => x.WeightMomentum).InclusiveBetween(0, 100).WithMessage(WeightMessage);
        RuleFor(x => x.WeightQuality).InclusiveBetween(0, 100).WithMessage(WeightMessage);
        RuleFor(x => x.WeightInsider).InclusiveBetween(0, 100).WithMessage(WeightMessage);
        RuleFor(x => x.WeightSentiment).InclusiveBetween(0, 100).WithMessage(WeightMessage);

        // Email / SMTP
        RuleFor(x => x.SmtpHost)
            .NotEmpty()
            .When(x => x.EmailEnabled)
            .WithMessage("SMTP host is required when email is enabled.");

        RuleFor(x => x.SmtpPort)
            .InclusiveBetween(1, 65535).WithMessage("Port must be between 1 and 65535.");

        RuleFor(x => x.SmtpFrom)
            .NotEmpty()
            .When(x => x.EmailEnabled, ApplyConditionTo.CurrentValidator)
            .WithMessage("From address is required when email is enabled.")
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.SmtpFrom), ApplyConditionTo.CurrentValidator)
            .WithMessage("Enter a valid email address.");

        RuleFor(x => x.SmtpTo)
            .NotEmpty()
            .When(x => x.EmailEnabled, ApplyConditionTo.CurrentValidator)
            .WithMessage("To address is required when email is enabled.")
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.SmtpTo), ApplyConditionTo.CurrentValidator)
            .WithMessage("Enter a valid email address.");

        // Holdings auto-import
        RuleFor(x => x.HoldingsCsvUrl)
            .Must(BeAValidHttpUrl!)
            .When(x => !string.IsNullOrWhiteSpace(x.HoldingsCsvUrl))
            .WithMessage("Enter a valid http(s) URL.");
    }

    private const string WeightMessage = "Weights must be between 0 and 100.";

    private static bool BeAKnownTimeZone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    private static bool BeAValidHttpUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
