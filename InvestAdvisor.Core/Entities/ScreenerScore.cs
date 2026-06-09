namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A daily snapshot of one stock's composite score and rank. Persisted once per day by the
/// screener worker so "Today's top picks" can highlight names whose score improved vs. the
/// prior day rather than surfacing the same leaders every time.
/// </summary>
public class ScreenerScore
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public DateTime AsOfDate { get; set; }   // UTC date (midnight) the snapshot was taken
    public decimal CompositeScore { get; set; }
    public int Rank { get; set; }

    /// <summary>Close/price captured at snapshot time, so forward returns (for the score-vs-return
    /// validation) can be computed from two snapshots without a separate price history table.</summary>
    public decimal? Price { get; set; }
}
