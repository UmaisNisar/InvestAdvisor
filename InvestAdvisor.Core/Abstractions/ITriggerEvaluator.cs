using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface ITriggerEvaluator
{
    /// <summary>
    /// Decides whether a run should fire on this tick. Returns null = do not fire.
    /// Priority: Manual &gt; PriceTarget &gt; BigMove &gt; Drift &gt; Scheduled. First match wins.
    /// </summary>
    RunTrigger? Evaluate(EvaluationInput input);
}

public sealed record EvaluationInput(
    DateTime NowUtc,
    DateTime? LastRunUtc,
    int RunsToday,
    Profile Profile,
    RuntimeSettings Settings,
    IReadOnlyList<Holding> Holdings,
    IReadOnlyList<WatchlistItem> Watchlist,
    IReadOnlyDictionary<string, PriceSnapshot> LatestSnapshotsByTicker,
    bool ManualOverride = false,
    string? ManualNote = null);
