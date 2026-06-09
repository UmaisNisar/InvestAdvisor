using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Decides whether the worker should fire an agent run on this tick. Pure logic — no DB,
/// no HTTP. Priority (first match wins):
/// 1) Manual override · 2) PriceTarget · 3) BigMove · 4) DriftThreshold · 5) Scheduled.
/// </summary>
public sealed class TriggerEvaluator : ITriggerEvaluator
{
    public RunTrigger? Evaluate(EvaluationInput input)
    {
        // 1) Manual override bypasses the min-gap and daily cap.
        if (input.ManualOverride)
        {
            var note = string.IsNullOrWhiteSpace(input.ManualNote) ? "Manual run" : input.ManualNote!;
            return new RunTrigger(RunTriggerKind.Manual, note);
        }

        // 2) Hard rate-limit.
        if (input.LastRunUtc is { } last
            && (input.NowUtc - last).TotalSeconds < input.Settings.MinSecondsBetweenRuns)
            return null;

        // 3) Daily cap.
        if (input.RunsToday >= input.Settings.MaxRunsPerDay) return null;

        // 4) Market-hours gate. Applies to equity/ETF triggers; crypto trades 24/7.
        var marketOpen = MarketHours.IsOpenNY(input.NowUtc, input.Settings.TimeZoneId);
        var equityTriggersAllowed = !input.Settings.MarketHoursOnly || marketOpen;

        var maxSnapAge = TimeSpan.FromSeconds(input.Settings.MaxSnapshotAgeForTriggerSeconds);
        bool SnapshotFresh(PriceSnapshot s) => (input.NowUtc - s.FetchedAtUtc) <= maxSnapAge;
        bool TriggerAllowedFor(AssetClass ac) =>
            ac == AssetClass.Crypto || equityTriggersAllowed;

        // 5) Watchlist price target hit (highest-priority real trigger).
        foreach (var w in input.Watchlist)
        {
            if (!input.LatestSnapshotsByTicker.TryGetValue(w.Ticker, out var snap)) continue;
            if (!SnapshotFresh(snap)) continue;
            if (!TriggerAllowedFor(w.AssetClass)) continue;

            if (w.PriceTargetLow is { } low && snap.Price <= low)
                return new RunTrigger(RunTriggerKind.PriceTarget,
                    $"{w.Ticker} crossed below {low:C} at {snap.Price:C}");

            if (w.PriceTargetHigh is { } high && snap.Price >= high)
                return new RunTrigger(RunTriggerKind.PriceTarget,
                    $"{w.Ticker} crossed above {high:C} at {snap.Price:C}");
        }

        // 6) Big single-day move on any held position.
        var moveThreshold = input.Profile.SingleDayMovePctThreshold;
        foreach (var h in input.Holdings)
        {
            if (!input.LatestSnapshotsByTicker.TryGetValue(h.Ticker, out var snap)) continue;
            if (!SnapshotFresh(snap)) continue;
            if (!TriggerAllowedFor(h.AssetClass)) continue;

            if (Math.Abs(snap.PercentChange) >= moveThreshold)
                return new RunTrigger(RunTriggerKind.BigMove,
                    $"{h.Ticker} moved {snap.PercentChange:+0.##;-0.##}% today");
        }

        // 7) Drift threshold breach (worst absolute drift across holdings with targets).
        if (input.Profile.DriftPctThreshold > 0m)
        {
            var drifts = ComputeDrifts(input.Holdings, input.LatestSnapshotsByTicker);
            if (drifts.Count > 0)
            {
                var worst = drifts.OrderByDescending(d => Math.Abs(d.DriftPct)).First();
                if (Math.Abs(worst.DriftPct) >= input.Profile.DriftPctThreshold
                    && TriggerAllowedFor(LookupAssetClass(input.Holdings, worst.Ticker)))
                    return new RunTrigger(RunTriggerKind.DriftThreshold,
                        $"{worst.Ticker} drift {worst.DriftPct:+0.##;-0.##}% vs target {worst.TargetPct}%");
            }
        }

        // 8) Scheduled cadence.
        var cadence = TimeSpan.FromHours(input.Profile.RebalanceCadenceHours);
        var scheduledDue = input.LastRunUtc is null
            || (input.NowUtc - input.LastRunUtc.Value) >= cadence;
        if (scheduledDue)
        {
            // Always allow at least one scheduled run path; the equity-vs-crypto gate
            // only bites real condition triggers, not the catch-up cadence.
            return new RunTrigger(RunTriggerKind.Scheduled,
                $"Scheduled cadence ({input.Profile.RebalanceCadenceHours}h) elapsed");
        }

        return null;
    }

    private static List<DriftRow> ComputeDrifts(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots)
    {
        var totalMv = 0m;
        var perHolding = new List<(Holding h, decimal mv)>();
        foreach (var h in holdings)
        {
            if (!snapshots.TryGetValue(h.Ticker, out var snap)) continue;
            var mv = h.Quantity * snap.Price;
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
