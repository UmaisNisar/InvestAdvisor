using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Tags each flag, position call, and drift alert with <c>KnownTicker</c>: false when the LLM
/// mentioned a ticker that is neither in the user's holdings nor watchlist. The UI surfaces
/// this so hallucinated tickers are visually obvious without breaking the run.
/// </summary>
public static class TickerHallucinationValidator
{
    public static AgentAnalysis Validate(AgentAnalysis analysis, IEnumerable<string> knownTickers)
    {
        var set = new HashSet<string>(knownTickers, StringComparer.OrdinalIgnoreCase);

        var validatedFlags = analysis.Flags
            .Select(f => f with { KnownTicker = f.Ticker is null || set.Contains(f.Ticker) })
            .ToArray();

        var validatedPositions = analysis.Positions
            .Select(p => p with { KnownTicker = set.Contains(p.Ticker) })
            .ToArray();

        var validatedDrift = analysis.DriftAlerts
            .Select(d => d with { KnownTicker = set.Contains(d.Ticker) })
            .ToArray();

        return analysis with
        {
            Flags = validatedFlags,
            Positions = validatedPositions,
            DriftAlerts = validatedDrift,
        };
    }
}
