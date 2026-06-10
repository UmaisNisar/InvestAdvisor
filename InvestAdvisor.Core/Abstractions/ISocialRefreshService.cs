namespace InvestAdvisor.Core.Abstractions;

public interface ISocialRefreshService
{
    /// <summary>
    /// Pulls recent social posts (StockTwits, Reddit, …) for the screener universe plus tracked
    /// tickers, de-dups against existing <c>NewsItem</c> rows by URL, and persists what's new as
    /// unscored rows for the sentiment scorer to pick up. Respects an internal cadence. Returns the
    /// count written.
    /// </summary>
    Task<int> RefreshAsync(CancellationToken ct = default);
}
