using InvestAdvisor.Core.Swing; // shared Indicators math

namespace InvestAdvisor.Core.Momentum;

/// <summary>
/// Turns one ticker's bars into a <see cref="MomentumFeatures"/> reading and a concrete
/// <see cref="MomentumSetup"/>. Strategy: high-volatility volatility-expansion breakout — long only
/// among names volatile enough to move ~10% in a few sessions, above their intermediate trend, when
/// price breaks a tight consolidation base on a volume surge, with a tight ATR stop and an asymmetric
/// target. Pure and deterministic, so a setup generated live is computed identically to one replayed
/// in the backtest.
/// </summary>
public static class MomentumSignalBuilder
{
    /// <summary>
    /// Builds the feature reading + trade plan for <paramref name="input"/>, or null when there aren't
    /// enough bars for the trend SMA / ATR. The setup's <c>AsOfUtc</c> is the latest bar.
    /// </summary>
    public static (MomentumFeatures Features, MomentumSetup Setup)? Build(MomentumInput input, MomentumParams p)
    {
        var candles = input.Candles;
        var warmup = Math.Max(Math.Max(p.TrendSmaPeriod, p.BreakoutLookback), p.AtrPeriod) + 1;
        if (candles.Count < warmup) return null;

        var closes = Indicators.Closes(candles);
        var atr = Indicators.Atr(candles, p.AtrPeriod);
        if (atr is not { } atrValue || atrValue <= 0m) return null; // no volatility estimate → no risk-bounded plan

        var latest = candles[^1];

        var features = new MomentumFeatures(
            Close: latest.Close,
            TrendSma: Indicators.Sma(closes, p.TrendSmaPeriod),
            Atr: atrValue,
            AtrPercent: MomentumIndicators.AtrPercent(candles, p.AtrPeriod),
            BreakoutStrength: Indicators.BreakoutStrength(candles, p.BreakoutLookback),
            BaseRangePct: MomentumIndicators.ConsolidationRangePct(candles, p.BasePeriod),
            RelativeVolume: Indicators.RelativeVolume(candles, p.VolumeLookback),
            Rsi: Indicators.Rsi(closes, p.RsiPeriod),
            MomentumReturn: Indicators.Return(candles, p.MomentumLookback),
            AverageDollarVolume: Indicators.AverageDollarVolume(candles, p.VolumeLookback));

        var entryRef = latest.Close;
        // Symmetric ±0.3% band so the zone's mid is the close — keeps EntryReference (and realized R)
        // consistent with the plan.
        var entryLow = entryRef * 0.997m;
        var entryHigh = entryRef * 1.003m;

        var stop = entryRef - p.StopAtrMultiple * atrValue;
        if (stop <= 0m) stop = entryRef * 0.5m; // degenerate guard; keeps risk math finite
        var target = entryRef + p.TargetAtrMultiple * atrValue;

        var riskPerShare = entryRef - stop;
        var stopDistancePct = entryRef == 0m ? 0m : riskPerShare / entryRef * 100m;
        var positionPct = stopDistancePct <= 0m
            ? 0m
            : Math.Min(p.MaxPositionPct, p.RiskPerTradePct / stopDistancePct * 100m);

        // Which trigger fired (a squeeze breakout outranks the gentler continuation when both do).
        var kind = IsSqueezeBreakout(features, p) ? MomentumSetupKind.SqueezeBreakout
            : IsContinuation(features, p) ? MomentumSetupKind.Continuation
            : MomentumSetupKind.None;

        var setup = new MomentumSetup(
            Ticker: input.Ticker,
            Name: input.Name,
            AsOfUtc: latest.Time,
            EntryLow: Math.Round(entryLow, 4),
            EntryHigh: Math.Round(entryHigh, 4),
            StopLoss: Math.Round(stop, 4),
            Target: Math.Round(target, 4),
            RewardRiskRatio: Math.Round(p.RewardRiskRatio, 2),
            HoldingDays: p.HoldingDays,
            PositionSizePct: Math.Round(positionPct, 2),
            Kind: kind,
            Rationale: Rationale(features, p));

        return (features, setup);
    }

    /// <summary>
    /// A long entry qualifies — in a high-volatility name above its intermediate trend — on either a
    /// squeeze breakout (tight base + volume surge) or, when enabled, a strong momentum continuation
    /// breaking out. Non-qualifying names are still scored for ranking/watchlist context.
    /// </summary>
    public static bool Qualifies(MomentumFeatures f, MomentumParams p) =>
        f.AboveTrend && f.Atr is > 0m && f.IsHighVolatility(p)
        && (IsSqueezeBreakout(f, p) || IsContinuation(f, p));

    /// <summary>Break of a tight base (a coiled "squeeze") on above-average volume — the stronger signal.</summary>
    public static bool IsSqueezeBreakout(MomentumFeatures f, MomentumParams p) =>
        f.BreakoutStrength is { } b && b > p.BreakoutMargin
        && f.BaseRangeAtr is { } range && range <= p.MaxBaseRangeAtr
        && f.RelativeVolume is { } rv && rv >= p.MinRelativeVolume;

    /// <summary>Strong trailing momentum carrying through a fresh breakout — gentler, no tight base required.</summary>
    public static bool IsContinuation(MomentumFeatures f, MomentumParams p) =>
        p.EnableContinuation
        && f.BreakoutStrength is { } b && b > p.BreakoutMargin
        && f.MomentumReturn is { } m && m >= p.MinContinuationReturn
        && f.RelativeVolume is { } rv && rv >= p.MinRelativeVolume;

    private static string Rationale(MomentumFeatures f, MomentumParams p)
    {
        var parts = new List<string>();
        if (f.BreakoutStrength is { } b and > 0m) parts.Add($"broke {p.BreakoutLookback}-day high (+{b * 100m:0.0}%)");
        if (IsSqueezeBreakout(f, p) && f.BaseRangeAtr is { } range)
            parts.Add($"out of a {range:0.0}×ATR base (squeeze)");
        else if (IsContinuation(f, p) && f.MomentumReturn is { } m)
            parts.Add($"+{m * 100m:0.0}% {p.MomentumLookback}-day momentum");
        if (f.RelativeVolume is { } rv and > 1.3m) parts.Add($"{rv:0.0}× volume");
        if (f.AtrPercent is { } a) parts.Add($"{a * 100m:0.0}% daily range");
        return (parts.Count == 0 ? "No clear edge" : string.Join(", ", parts))
            + $". Ride the expansion: target +{p.TargetAtrMultiple:0.0}×ATR, stop −{p.StopAtrMultiple:0.0}×ATR, hold ≤{p.HoldingDays} sessions.";
    }
}
