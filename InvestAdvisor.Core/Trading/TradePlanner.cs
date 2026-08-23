using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Trading;

/// <summary>
/// Turns one ticker's bars into a feature reading and a concrete <see cref="TradeSetup"/> for any
/// strategy. The strategy supplies the readings and the trigger rules; the exit geometry, position
/// sizing and rounding live here once so every strategy's plan — live or replayed in the backtest —
/// is computed identically.
/// </summary>
public static class TradePlanner
{
    /// <summary>Builds the features + plan for <paramref name="input"/>, or null when the strategy can't read the bars.</summary>
    public static (StrategyFeatures Features, TradeSetup Setup)? Build(IStrategy strategy, StrategyInput input, StrategyParams p)
    {
        var candles = input.Candles;
        if (candles.Count < strategy.Warmup(p)) return null;

        var features = strategy.Read(candles, p);
        if (features is null || features.Atr <= 0m) return null; // no volatility estimate → no risk-bounded plan

        var latest = candles[^1];
        var entryRef = latest.Close;
        // Symmetric ±0.3% band so the zone's mid is the close — keeps EntryReference (and realized R)
        // consistent with the plan.
        var entryLow = entryRef * 0.997m;
        var entryHigh = entryRef * 1.003m;

        var stop = entryRef - p.StopAtrMultiple * features.Atr;
        if (stop <= 0m) stop = entryRef * 0.5m; // degenerate guard; keeps risk math finite
        var target = entryRef + p.TargetAtrMultiple * features.Atr;

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
            Kind: strategy.KindOf(features, p),
            Rationale: strategy.Rationale(features, p));

        return (features, setup);
    }
}
