using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

public interface IPriceRefreshService
{
    /// <summary>
    /// For each ticker, persists a new PriceSnapshot when the latest snapshot is older
    /// than <c>MinPriceFreshnessSeconds</c>. Returns the count of new snapshots written.
    /// </summary>
    Task<int> RefreshAsync(IEnumerable<TickerSpec> tickers, CancellationToken ct = default);
}

public sealed record TickerSpec(string Ticker, AssetClass AssetClass);
