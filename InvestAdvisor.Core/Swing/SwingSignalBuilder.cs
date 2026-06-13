using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Swing;

/// <summary>
/// Turns one ticker's bars into a <see cref="SwingFeatures"/> reading and a concrete
/// <see cref="TradeSetup"/>. Strategy: regime-filtered mean reversion — long only when price is above
/// its 200-day SMA (confirmed up-trend) and short-term oversold (low RSI(3)), targeting the bounce
/// with a wide ATR stop. Pure and deterministic, so a setup generated live is computed identically to
/// one replayed in the backtest.
/// </summary>
public static class SwingSignalBuilder
{
    /// <summary>
    /// Builds the feature reading + trade plan for <paramref name="input"/>, or null when there
    /// aren't enough bars for the regime SMA / ATR. The setup's <c>AsOfUtc</c> is the latest bar.
    /// </summary>
    public static (SwingFeatures Features, TradeSetup Setup)? Build(SwingInput input, SwingParams p)
    {
        var candles = input.Candles;
        if (candles.Count < p.RegimeSmaPeriod + 1) return null; // need a full 200-day regime window

        var closes = Indicators.Closes(candles);
        var atr = Indicators.Atr(candles, p.AtrPeriod);
        if (atr is not { } atrValue || atrValue <= 0m) return null; // no volatility estimate → no risk-bounded plan

        var latest = candles[^1];
        var regimeSma = Indicators.Sma(closes, p.RegimeSmaPeriod);
        var pullbackSma = Indicators.Sma(closes, p.PullbackSmaPeriod);
        decimal? pullbackPct = pullbackSma is { } ps && ps > 0m ? (ps - latest.Close) / ps : null;

        var features = new SwingFeatures(
            Close: latest.Close,
            RegimeSma: regimeSma,
            Rsi: Indicators.Rsi(closes, p.RsiPeriod),
            PullbackPct: pullbackPct,
            RelativeVolume: Indicators.RelativeVolume(candles, p.VolumeLookback),
            Atr: atrValue,
            AverageDollarVolume: Indicators.AverageDollarVolume(candles, p.VolumeLookback));

        var entryRef = latest.Close;
        // Symmetric ±0.3% band so the zone's mid is the close — keeps EntryReference (and realized R)
        // consistent with the plan.
        var entryLow = entryRef * 0.997m;
        var entryHigh = entryRef * 1.003m;

        var stop = entryRef - p.AtrStopMultiple * atrValue;
        if (stop <= 0m) stop = entryRef * 0.5m; // degenerate guard; keeps risk math finite
        var target = entryRef + p.TargetAtrMultiple * atrValue;

        var riskPerShare = entryRef - stop;
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
            RewardRiskRatio: Math.Round(p.RewardRiskRatio, 2),
            HoldingDays: p.HoldingDays,
            PositionSizePct: Math.Round(positionPct, 2),
            Rationale: Rationale(features, p));

        return (features, setup);
    }

    /// <summary>
    /// A long entry qualifies only when the name is in a confirmed long-term up-trend (above the
    /// 200-day SMA) <i>and</i> short-term oversold (RSI at/below the threshold) — i.e. a pullback to
    /// buy, not strength to chase. Non-qualifying names still get scored, for ranking context.
    /// </summary>
    public static bool Qualifies(SwingFeatures f, SwingParams p) =>
        f.AboveRegime
        && f.Rsi is { } r && r <= p.OversoldEntry
        && f.Atr is > 0m;

    private static string Rationale(SwingFeatures f, SwingParams p)
    {
        var parts = new List<string>();
        if (f.RegimeDistancePct is { } d) parts.Add($"{d * 100m:0.0}% above 200-day MA (up-trend)");
        if (f.Rsi is { } r) parts.Add($"RSI({p.RsiPeriod}) {r:0} — oversold pullback");
        if (f.PullbackPct is { } pb and > 0m) parts.Add($"{pb * 100m:0.0}% below its {p.PullbackSmaPeriod}-day average");
        if (f.RelativeVolume is { } rv and > 1.3m) parts.Add($"{rv:0.0}× volume");
        return (parts.Count == 0 ? "No clear edge" : string.Join(", ", parts))
            + $". Buy the bounce: target +{p.TargetAtrMultiple:0.0}×ATR, stop −{p.AtrStopMultiple:0.0}×ATR, hold ≤{p.HoldingDays} sessions.";
    }
}
