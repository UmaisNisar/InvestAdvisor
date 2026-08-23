namespace InvestAdvisor.Core.Scoring;

/// <summary>
/// The percentile-rank → 0–100 sub-score → weighted composite machinery shared by every ranking
/// engine (fundamental screener, swing, momentum). Pure functions over an arbitrary row type so
/// each engine keeps its own feature model but none re-implements the math.
/// </summary>
public static class PercentileRanker
{
    /// <summary>
    /// Percentile rank in [0, 1] of each row's selected value across the rows that have one.
    /// Ties share the average of the positions they span; a lone value ranks 0.5. Rows whose
    /// selector returns null are omitted from the result.
    /// </summary>
    public static Dictionary<string, decimal> Rank<T>(
        IReadOnlyList<T> rows, Func<T, string> key, Func<T, decimal?> select, bool higherBetter)
    {
        var present = new List<(string Key, decimal V)>(rows.Count);
        foreach (var r in rows)
            if (select(r) is { } v) present.Add((key(r), v));

        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var n = present.Count;
        if (n == 0) return result;
        if (n == 1) { result[present[0].Key] = 0.5m; return result; }
        foreach (var (k, v) in present)
        {
            var better = present.Count(x => higherBetter ? x.V < v : x.V > v);
            var equal = present.Count(x => x.V == v);
            var rank = (better + (equal - 1) / 2m) / (n - 1);
            result[k] = Math.Clamp(rank, 0m, 1m);
        }
        return result;
    }

    /// <summary>A key's [0, 1] rank, or null when the row had no value for that factor.</summary>
    public static decimal? Get(Dictionary<string, decimal> ranks, string key) =>
        ranks.TryGetValue(key, out var r) ? r : null;

    /// <summary>[0, 1] rank → 0–100 sub-score (1 dp).</summary>
    public static decimal? Scale(decimal? rank01) =>
        rank01.HasValue ? Math.Round(rank01.Value * 100m, 1) : null;

    /// <summary>Looks up a key's rank and scales it to a 0–100 sub-score; null when absent.</summary>
    public static decimal? SubScore(Dictionary<string, decimal> ranks, string key) => Scale(Get(ranks, key));

    /// <summary>Mean of the ranks that have data; null when none do.</summary>
    public static decimal? Avg(params decimal?[] vals)
    {
        decimal acc = 0m; var n = 0;
        foreach (var v in vals) if (v.HasValue) { acc += v.Value; n++; }
        return n == 0 ? null : acc / n;
    }

    /// <summary>
    /// Weighted mean of the sub-scores that have data, renormalised over the weights present.
    /// Null (not 0) when nothing has data — a 0 would rank a data-gap name as the worst in the
    /// universe rather than excluding it.
    /// </summary>
    public static decimal? Composite(params (decimal? Score, decimal Weight)[] parts)
    {
        decimal acc = 0m, wsum = 0m;
        foreach (var (sc, w) in parts)
            if (sc.HasValue) { acc += sc.Value * w; wsum += w; }
        return wsum > 0m ? Math.Round(acc / wsum, 1) : null;
    }
}
