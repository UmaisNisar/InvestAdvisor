using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface ITriggerEvaluator
{
    /// <summary>
    /// Decides whether a run should fire on this tick. Condition triggers (PriceTarget,
    /// BigMove, Drift) are <em>edge-triggered</em>: a condition fires once when it first
    /// breaches and is then suppressed until it clears and re-breaches, so a persistent move
    /// (e.g. a stock down -15% all session) does not burn a Claude call every tick.
    /// Priority: Manual &gt; PriceTarget &gt; BigMove &gt; Drift &gt; Scheduled. First match wins.
    /// </summary>
    TriggerDecision Evaluate(EvaluationInput input);
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
    string? ManualNote = null,
    /// <summary>
    /// Dedup keys of condition triggers already alerted on and not yet re-armed. The caller
    /// passes back the <see cref="TriggerDecision.ActiveKeys"/> from the previous tick.
    /// </summary>
    IReadOnlySet<string>? SuppressedKeys = null);

/// <summary>
/// Result of one evaluation. <see cref="ActiveKeys"/> is the dedup-key set the caller must
/// carry into the next tick's <see cref="EvaluationInput.SuppressedKeys"/>: it contains every
/// condition that is still breached and has been alerted (so it stays suppressed), minus any
/// that cleared (re-armed), plus the one fired this tick. Non-condition triggers
/// (Manual, Scheduled) have no key and never enter the set.
/// </summary>
public sealed record TriggerDecision(RunTrigger? Trigger, IReadOnlySet<string> ActiveKeys);
