using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Data.Providers.Finnhub;
using InvestAdvisor.Data.Providers.Yahoo;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers;

/// <summary>
/// Routes quote requests to the right source, since no single free provider covers everything:
/// crypto → CoinGecko (Finnhub's crypto candles are premium); non-US listings (exchange-suffixed
/// tickers like <c>.TO</c>/<c>.AX</c>) → Yahoo (Finnhub free is US-only); US equities/ETFs →
/// Finnhub, falling back to Yahoo if Finnhub returns nothing.
/// </summary>
public sealed class CompositeMarketDataProvider(
    FinnhubMarketDataProvider finnhub,
    YahooQuoteProvider yahoo,
    ICryptoMarketProvider coinGecko,
    ISystemClock clock,
    ILogger<CompositeMarketDataProvider>? logger = null) : IMarketDataProvider, IPriceHistoryProvider
{
    public async Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct = default)
    {
        if (assetClass == AssetClass.Crypto)
            return await GetCryptoQuoteAsync(ticker, ct);

        // Exchange-suffixed tickers (e.g. IDIV.B.TO) are non-US → Yahoo.
        if (ticker.Contains('.'))
            return await yahoo.GetQuoteAsync(ticker, assetClass, ct);

        // US equities/ETFs via Finnhub; if it has nothing, try Yahoo as a backup.
        return await finnhub.GetQuoteAsync(ticker, assetClass, ct)
            ?? await yahoo.GetQuoteAsync(ticker, assetClass, ct);
    }

    /// <summary>
    /// All history comes from Yahoo's chart endpoint: it covers US and non-US listings, and crypto
    /// via the <c>{TICKER}-USD</c> pair. Finnhub's candle endpoints are premium, so there's nothing
    /// to route around here — only the crypto symbol needs the USD pair suffix.
    /// </summary>
    public async Task<PriceHistory?> GetHistoryAsync(string ticker, AssetClass assetClass, HistoryRange range, CancellationToken ct = default)
    {
        var symbol = assetClass == AssetClass.Crypto ? $"{ticker}-USD" : ticker;
        var history = await yahoo.GetHistoryAsync(symbol, assetClass, range, ct);
        // Report under the user's own ticker, not the routed -USD pair.
        return history is null ? null : history with { Ticker = ticker.ToUpperInvariant() };
    }

    private async Task<Quote?> GetCryptoQuoteAsync(string ticker, CancellationToken ct)
    {
        var id = CryptoIds.ToCoinGeckoId(ticker);
        try
        {
            var markets = await coinGecko.GetMarketsAsync(new[] { id }, ct);
            if (markets.FirstOrDefault() is { Price: > 0m } m)
            {
                var pct = m.Change24hPct ?? 0m;
                var denom = 1m + pct / 100m;
                var prev = denom == 0m ? m.Price!.Value : m.Price!.Value / denom;
                return new Quote(ticker.ToUpperInvariant(), AssetClass.Crypto, m.Price!.Value, prev, pct, clock.UtcNow);
            }
            logger?.LogInformation("CoinGecko returned no price for {Ticker} ({Id}).", ticker, id);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "CoinGecko quote failed for {Ticker}.", ticker); }
        return null;
    }
}

/// <summary>Maps a user's crypto ticker (BTC) to a CoinGecko coin id (bitcoin).</summary>
internal static class CryptoIds
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "bitcoin", ["ETH"] = "ethereum", ["BNB"] = "binancecoin", ["SOL"] = "solana",
        ["XRP"] = "ripple", ["ADA"] = "cardano", ["DOGE"] = "dogecoin", ["AVAX"] = "avalanche-2",
        ["DOT"] = "polkadot", ["LINK"] = "chainlink", ["MATIC"] = "matic-network", ["LTC"] = "litecoin",
        ["USDT"] = "tether", ["USDC"] = "usd-coin", ["TRX"] = "tron", ["BCH"] = "bitcoin-cash",
        ["XLM"] = "stellar", ["ATOM"] = "cosmos", ["UNI"] = "uniswap", ["ETC"] = "ethereum-classic",
        ["NEAR"] = "near", ["APT"] = "aptos", ["ARB"] = "arbitrum", ["OP"] = "optimism",
        ["SHIB"] = "shiba-inu", ["PEPE"] = "pepe", ["TON"] = "the-open-network", ["ICP"] = "internet-computer",
    };

    public static string ToCoinGeckoId(string ticker) =>
        Map.TryGetValue(ticker.Trim(), out var id) ? id : ticker.Trim().ToLowerInvariant();
}
