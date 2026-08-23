using InvestAdvisor.Core.Scoring;

namespace InvestAdvisor.Core.Trading;

/// <summary>
/// Ranks a universe for any strategy: each of the strategy's factors is percentile-ranked across
/// the universe (so absolute units don't matter), scaled to a 0–100 sub-score, and weighted into a
/// composite; optional news/social sentiment joins as one more factor. Names below the liquidity
/// floor are dropped before ranking — the gate that keeps untradeable thin tickers out. Pure: no I/O.
/// </summary>
public static class StrategyScorer
{
    /// <summary>
    /// Scores every input with enough bars, best-first. Names that don't meet the entry conditions
    /// are still returned (ranked on their readings) but flagged <see cref="StrategyScore.Qualifies"/>=false.
    /// </summary>
    public static IReadOnlyList<StrategyScore> Rank(
        IStrategy strategy,
        IReadOnlyList<StrategyInput> universe,
        StrategyParams p,
        IReadOnlyDictionary<string, decimal>? sentimentByTicker = null)
    {
        var rows = new List<Row>(universe.Count);
        foreach (var input in universe)
        {
            var built = TradePlanner.Build(strategy, input, p);
            if (built is null) continue;
            var (features, setup) = built.Value;

            if (features.AverageDollarVolume is not { } adv || adv < p.MinAvgDollarVolume) continue;

            rows.Add(new Row(input, features, setup,
                sentimentByTicker is not null && sentimentByTicker.TryGetValue(input.Ticker, out var sv) ? sv : null));
        }
        if (rows.Count == 0) return Array.Empty<StrategyScore>();

        var ranks = strategy.Factors
            .Select(f => (f.Weight, Ranks: PercentileRanker.Rank(rows, r => r.Input.Ticker, r => f.Select(r.Features), f.HigherBetter)))
            .ToList();
        var sentimentRanks = PercentileRanker.Rank(rows, r => r.Input.Ticker, r => r.Sentiment, higherBetter: true);

        var scores = new List<StrategyScore>(rows.Count);
        foreach (var r in rows)
        {
            var parts = ranks
                .Select(x => (PercentileRanker.SubScore(x.Ranks, r.Input.Ticker), x.Weight))
                .Append((PercentileRanker.SubScore(sentimentRanks, r.Input.Ticker), strategy.SentimentWeight))
                .ToArray();
            var composite = PercentileRanker.Composite(parts);
            if (composite is null) continue;

            scores.Add(new StrategyScore(
                r.Input.Ticker, r.Input.Name, r.Input.Sector, composite.Value,
                r.Features, r.Setup, strategy.Qualifies(r.Features, p)));
        }

        return scores
            .OrderByDescending(s => s.CompositeScore)
            .ThenBy(s => s.Ticker, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record Row(StrategyInput Input, StrategyFeatures Features, TradeSetup Setup, decimal? Sentiment);
}
