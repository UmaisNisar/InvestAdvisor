using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Swing;

/// <summary>
/// Turns one ticker's bars into a <see cref="SwingFeatures"/> reading and a concrete
/// <see cref="TradeSetup"/> (entry zone, ATR stop, reward:risk target, position size). Pure and
/// deterministic — the live scorer and the backtest both call it, so a setup generated today is
/// computed identically to one replayed from history.
/// </summary>
public static class SwingSignalBuilder
{
    /// <summary>RSI sweet spot for a long swing entry: strong but not yet exhausted.</summary>
    private const decimal IdealRsi = 60m;

    /// <summary>
    /// Builds the feature reading + trade plan for <paramref name="input"/>, or null when there
    /// aren't enough bars for the core indicators (ATR / EMAs). The setup's <c>AsOfUtc</c> is the
    /// timestamp of the latest bar — the close the plan is anchored to.
    /// </summary>
    public static (SwingFeatures Features, TradeSetup Setup)? Build(SwingInput input, SwingParams p)
    {
        var candles = input.Candles;
        if (candles.Count < p.EmaSlowPeriod + 1) return null;

        var closes = Indicators.Closes(candles);
        var atr = Indicators.Atr(candles, p.AtrPeriod);
        if (atr is not { } atrValue || atrValue <= 0m) return null; // no volatility estimate → no risk-bounded plan

        var latest = candles[^1];
        var features = new SwingFeatures(
            Close: latest.Close,
            Rsi: Indicators.Rsi(closes, p.RsiPeriod),
            EmaFast: Indicators.Ema(closes, p.EmaFastPeriod),
            EmaSlow: Indicators.Ema(closes, p.EmaSlowPeriod),
            BreakoutStrength: Indicators.BreakoutStrength(candles, p.BreakoutLookback),
            RelativeVolume: Indicators.RelativeVolume(candles, p.VolumeLookback),
            Momentum: Indicators.Return(candles, p.MomentumSessions),
            Gap: Indicators.Gap(candles),
            Atr: atrValue,
            AverageDollarVolume: Indicators.AverageDollarVolume(candles, p.VolumeLookback));

        var entryRef = latest.Close;
        // Tight, symmetric entry band around the close (±0.3%) so the zone's mid is the close itself —
        // that keeps EntryReference, and therefore the realized reward:risk, consistent with the plan.
        var entryLow = entryRef * 0.997m;
        var entryHigh = entryRef * 1.003m;

        var stop = entryRef - p.AtrStopMultiple * atrValue;
        if (stop <= 0m) stop = entryRef * 0.5m; // degenerate guard; keeps risk math finite
        var riskPerShare = entryRef - stop;
        var target = entryRef + p.RewardRiskRatio * riskPerShare;

        var stopDistancePct = entryRef == 0m ? 0m : riskPerShare / entryRef * 100m;
        var positionPct = stopDistancePct <= 0m
            ? 0m
            : Math.Min(p.MaxPositionPct, p.RiskPerTradePct / stopDistancePct * 100m);

        var setup = new TradeSetup(
            Ticker: input.Ticker,
            Name: input.Name,
            AsOfUtc: latest.Time,
            EntryLow: Math.Round(entryLow, 4),
            EntryHigh: Math.Round(entryHigh, 4),
            StopLoss: Math.Round(stop, 4),
            Target: Math.Round(target, 4),
            RewardRiskRatio: p.RewardRiskRatio,
            HoldingDays: p.HoldingDays,
            PositionSizePct: Math.Round(positionPct, 2),
            Rationale: Rationale(features));

        return (features, setup);
    }

    /// <summary>
    /// A long swing entry "qualifies" only when the trend, momentum and RSI line up and the name
    /// isn't already over-extended. Non-qualifying names still get scored (for ranking context) but
    /// are not surfaced as actionable setups.
    /// </summary>
    public static bool Qualifies(SwingFeatures f) =>
        f.TrendUp
        && f.Rsi is >= 45m and < 75m              // momentum present, not exhausted
        && (f.Momentum is > 0m || f.BreakoutStrength is > 0m); // moving up or breaking out

    /// <summary>How close an RSI reading is to the long-entry sweet spot — higher is better.</summary>
    public static decimal RsiQuality(decimal? rsi) => rsi is { } r ? -Math.Abs(r - IdealRsi) : decimal.MinValue;

    private static string Rationale(SwingFeatures f)
    {
        var parts = new List<string>();
        if (f.TrendUp) parts.Add("9/21 EMA up-trend");
        if (f.BreakoutStrength is > 0m) parts.Add($"breakout +{f.BreakoutStrength.Value * 100m:0.0}% over 20-day high");
        if (f.Momentum is { } m) parts.Add($"{m * 100m:+0.0;-0.0}% 5-day momentum");
        if (f.Rsi is { } r) parts.Add($"RSI {r:0}");
        if (f.RelativeVolume is { } rv and > 1.2m) parts.Add($"{rv:0.0}× volume");
        return parts.Count == 0 ? "No clear edge" : string.Join(", ", parts) + ".";
    }
}
