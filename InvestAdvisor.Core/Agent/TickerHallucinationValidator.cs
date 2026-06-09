using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Tags each flag with <c>KnownTicker</c>: false when the LLM mentioned a ticker that is
/// neither in the user's holdings nor watchlist. The UI surfaces this so hallucinated
/// tickers are visually obvious without breaking the run.
/// </summary>
public static class TickerHallucinationValidator
{
    public static AgentAnalysis Validate(AgentAnalysis analysis, IEnumerable<string> knownTickers)
    {
        var set = new HashSet<string>(knownTickers, StringComparer.OrdinalIgnoreCase);

        var validatedFlags = analysis.Flags
            .Select(f => f with { KnownTicker = f.Ticker is null || set.Contains(f.Ticker) })
            .ToArray();

        return analysis with { Flags = validatedFlags };
    }
}
