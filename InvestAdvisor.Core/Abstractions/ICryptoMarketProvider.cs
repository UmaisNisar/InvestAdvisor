namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Crypto market data for the screener (CoinGecko free API): price, market cap, and 7-/30-day
/// momentum. One batched request covers the whole crypto universe.
/// </summary>
public interface ICryptoMarketProvider
{
    Task<IReadOnlyList<CryptoMarket>> GetMarketsAsync(IReadOnlyCollection<string> coinIds, CancellationToken ct = default);
}

public sealed record CryptoMarket(
    string CoinId,
    string Symbol,
    decimal? Price,
    decimal? MarketCap,
    decimal? Return7d,
    decimal? Return30d,
    decimal? Change24hPct = null);
