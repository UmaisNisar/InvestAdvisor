namespace InvestAdvisor.Core.Abstractions;

public interface INewsRefreshService
{
    /// <summary>
    /// Fetches recent ticker-specific and market-wide news, de-dups against existing
    /// <c>NewsItem</c> rows (by URL), and persists what's new. Returns the count written.
    /// Respects an internal cadence so news isn't refreshed on every worker tick.
    /// </summary>
    Task<int> RefreshAsync(IEnumerable<TickerSpec> tickers, CancellationToken ct = default);
}
