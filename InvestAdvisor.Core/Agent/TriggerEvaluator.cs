using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Decides whether the worker should fire an agent run on this tick. Pure logic — no DB,
/// no HTTP. Priority (first match wins):
/// 1) Manual override · 2) PriceTarget · 3) BigMove · 4) DriftThreshold · 5) Scheduled.
///
/// Condition triggers (2–4) are edge-triggered with a dedup key of <c>"{Kind}:{Ticker}"</c>:
/// a breached condition fires once, then stays suppressed (via <see cref="EvaluationInput.SuppressedKeys"/>)
/// until it clears and re-breaches. Without this a level condition — a stock down -15% for the
/// whole session — re-fires every <c>MinSecondsBetweenRuns</c> until the daily cap, each a Claude call.
/// </summary>
public sealed class TriggerEvaluator : ITriggerEvaluator
{
    private static readonly IReadOnlySet<string> Empty =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public TriggerDecision Evaluate(EvaluationInput input)
    {
        var suppressed = input.SuppressedKeys ?? Empty;

        // 1) Manual override bypasses the gates and the dedup machinery entirely.
        if (input.ManualOverride)
        {
            var note = string.IsNullOrWhiteSpace(input.ManualNote) ? "Manual run" : input.ManualNote!;
            return new TriggerDecision(new RunTrigger(RunTriggerKind.Manual, note), suppressed);
        }

        // Every condition breached on this tick, in priority order, paired with its dedup key.
        var candidates = CollectBreaches(input);
        var breachedKeys = candidates.Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Re-arm: drop previously-alerted keys whose condition is no longer breached. Done every
        // tick regardless of the firing gates below, so a clear is observed as soon as it happens.
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in suppressed)
            if (breachedKeys.Contains(key)) active.Add(key);

        // Firing gates (backstops). Re-arm bookkeeping above is preserved even when blocked.
        var blocked =
            (input.LastRunUtc is { } last
                && (input.NowUtc - last).TotalSeconds < input.Settings.MinSecondsBetweenRuns)
            || input.RunsToday >= input.Settings.MaxRunsPerDay;

        if (!blocked)
        {
            // First breached condition we have not already alerted on (the rising edge).
            foreach (var (key, trigger) in candidates)
            {
                if (active.Contains(key)) continue; // still-active alert → suppress
                active.Add(key);
                return new TriggerDecision(trigger, active);
            }

            // No new condition. Fall back to scheduled cadence (no dedup key).
            var scheduled = ScheduledTrigger(input);
            if (scheduled is not null)
                return new TriggerDecision(scheduled, active);
        }

        return new TriggerDecision(null, active);
    }

    /// <summary>
    /// All currently-breached condition triggers, ordered PriceTarget &gt; BigMove &gt; Drift,
    /// each tagged with its <c>"{Kind}:{Ticker}"</c> dedup key. Honours snapshot freshness and
    /// the market-hours gate (crypto trades 24/7; equity/ETF only when allowed).
    /// </summary>
    private static List<(string Key, RunTrigger Trigger)> CollectBreaches(EvaluationInput input)
    {
        var result = new List<(string, RunTrigger)>();

        var marketOpen = MarketHours.IsOpenNY(input.NowUtc, input.Settings.TimeZoneId);
        var equityTriggersAllowed = !input.Settings.MarketHoursOnly || marketOpen;
        var maxSnapAge = TimeSpan.FromSeconds(input.Settings.MaxSnapshotAgeForTriggerSeconds);
        bool SnapshotFresh(PriceSnapshot s) => (input.NowUtc - s.FetchedAtUtc) <= maxSnapAge;
        bool TriggerAllowedFor(AssetClass ac) => ac == AssetClass.Crypto || equityTriggersAllowed;

        static string Key(RunTriggerKind kind, string ticker) => $"{kind}:{ticker}";

        // 2) Watchlist price target hit (highest-priority real trigger).
        foreach (var w in input.Watchlist)
        {
            if (!input.LatestSnapshotsByTicker.TryGetValue(w.Ticker, out var snap)) continue;
            if (!SnapshotFresh(snap)) continue;
            if (!TriggerAllowedFor(w.AssetClass)) continue;

            if (w.PriceTargetLow is { } low && snap.Price <= low)
                result.Add((Key(RunTriggerKind.PriceTarget, w.Ticker),
                    new RunTrigger(RunTriggerKind.PriceTarget,
                        $"{w.Ticker} crossed below {low:C} at {snap.Price:C}", w.Ticker)));
            else if (w.PriceTargetHigh is { } high && snap.Price >= high)
                result.Add((Key(RunTriggerKind.PriceTarget, w.Ticker),
                    new RunTrigger(RunTriggerKind.PriceTarget,
                        $"{w.Ticker} crossed above {high:C} at {snap.Price:C}", w.Ticker)));
        }

        // 3) Big single-day move on any held position.
        var moveThreshold = input.Profile.SingleDayMovePctThreshold;
        foreach (var h in input.Holdings)
        {
            if (!input.LatestSnapshotsByTicker.TryGetValue(h.Ticker, out var snap)) continue;
            if (!SnapshotFresh(snap)) continue;
            if (!TriggerAllowedFor(h.AssetClass)) continue;

            if (Math.Abs(snap.PercentChange) >= moveThreshold)
                result.Add((Key(RunTriggerKind.BigMove, h.Ticker),
                    new RunTrigger(RunTriggerKind.BigMove,
                        $"{h.Ticker} moved {snap.PercentChange:+0.##;-0.##}% today", h.Ticker)));
        }

        // 4) Drift threshold breach (worst absolute drift across holdings with targets).
        if (input.Profile.DriftPctThreshold > 0m)
        {
            var drifts = ComputeDrifts(
                input.Holdings, input.LatestSnapshotsByTicker, input.FxRatesToUsd, SnapshotFresh);
            if (drifts.Count > 0)
            {
                var worst = drifts.OrderByDescending(d => Math.Abs(d.DriftPct)).First();
                if (Math.Abs(worst.DriftPct) >= input.Profile.DriftPctThreshold
                    && TriggerAllowedFor(LookupAssetClass(input.Holdings, worst.Ticker)))
                    result.Add((Key(RunTriggerKind.DriftThreshold, worst.Ticker),
                        new RunTrigger(RunTriggerKind.DriftThreshold,
                            $"{worst.Ticker} drift {worst.DriftPct:+0.##;-0.##}% vs target {worst.TargetPct}%",
                            worst.Ticker)));
            }
        }

        return result;
    }

    private static RunTrigger? ScheduledTrigger(EvaluationInput input)
    {
        var cadence = TimeSpan.FromHours(input.Profile.RebalanceCadenceHours);
        var scheduledDue = input.LastRunUtc is null
            || (input.NowUtc - input.LastRunUtc.Value) >= cadence;
        return scheduledDue
            ? new RunTrigger(RunTriggerKind.Scheduled,
                $"Scheduled cadence ({input.Profile.RebalanceCadenceHours}h) elapsed")
            : null;
    }

    private static List<DriftRow> ComputeDrifts(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal>? fxRatesToUsd,
        Func<PriceSnapshot, bool> snapshotFresh)
    {
        // Allocation shares need a common denominator: convert each native-currency market value
        // to USD. Stale snapshots are skipped — a drift computed off an old price would fire a
        // run on numbers the user no longer sees anywhere else in the app.
        var totalMv = 0m;
        var perHolding = new List<(Holding h, decimal mv)>();
        foreach (var h in holdings)
        {
            if (!snapshots.TryGetValue(h.Ticker, out var snap)) continue;
            if (!snapshotFresh(snap)) continue;
            var currency = string.IsNullOrWhiteSpace(h.Currency) ? "USD" : h.Currency.Trim();
            var rate = fxRatesToUsd is not null && fxRatesToUsd.TryGetValue(currency, out var r) ? r : 1m;
            var mv = h.Quantity * snap.Price * rate;
            totalMv += mv;
            perHolding.Add((h, mv));
        }
        if (totalMv <= 0m) return new List<DriftRow>();

        var result = new List<DriftRow>();
        foreach (var (h, mv) in perHolding)
        {
            if (h.TargetAllocationPct is null) continue;
            var current = (mv / totalMv) * 100m;
            var drift = current - h.TargetAllocationPct.Value;
            result.Add(new DriftRow(h.Ticker, current, h.TargetAllocationPct.Value, drift));
        }
        return result;
    }

    private static AssetClass LookupAssetClass(IReadOnlyList<Holding> holdings, string ticker) =>
        holdings.FirstOrDefault(h => string.Equals(h.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
                ?.AssetClass ?? AssetClass.Equity;
}
