namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Anthropic list pricing (USD per 1M tokens), keyed by model-id prefix. Used to turn the
/// InputTokens/OutputTokens already persisted on each run into a dollar estimate, so the daily
/// budget guard and the cost panel agree on the same number. Unknown ids fall back to Sonnet rates.
/// </summary>
public static class ModelPricing
{
    private static readonly (string Prefix, decimal InPerM, decimal OutPerM)[] Table =
    {
        ("claude-opus", 5m, 25m),
        ("claude-sonnet", 3m, 15m),
        ("claude-haiku", 1m, 5m),
    };

    public static decimal EstimateUsd(string? model, long inputTokens, long outputTokens)
    {
        var (inPerM, outPerM) = RateFor(model);
        return inputTokens / 1_000_000m * inPerM + outputTokens / 1_000_000m * outPerM;
    }

    private static (decimal InPerM, decimal OutPerM) RateFor(string? model)
    {
        var m = model ?? string.Empty;
        foreach (var (prefix, inP, outP) in Table)
            if (m.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (inP, outP);
        return (3m, 15m); // default to Sonnet rates for an unrecognized id
    }
}
