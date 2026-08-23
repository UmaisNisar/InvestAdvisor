using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Finnhub;

/// <summary>
/// Reads quotes from Finnhub:
/// equities/ETFs via <c>/quote?symbol=...</c>;
/// crypto via <c>/crypto/candle?symbol=BINANCE:XXXUSDT&amp;resolution=D&amp;from=...&amp;to=...</c>
/// because /quote does not reliably return crypto prices.
/// Each call goes through the shared <see cref="IRateLimiter"/>.
/// </summary>
public sealed class FinnhubMarketDataProvider(
    HttpClient http,
    IRateLimiter rateLimiter,
    CryptoSymbolRouter symbolRouter,
    ISystemClock clock,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubMarketDataProvider>? logger = null) : IMarketDataProvider
{
    private readonly FinnhubApi _api = new(http, rateLimiter, options.Value, logger);

    public Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct = default)
    {
        _api.EnsureKey();
        return assetClass == AssetClass.Crypto
            ? GetCryptoQuoteAsync(ticker, ct)
            : GetEquityQuoteAsync(ticker, assetClass, ct);
    }

    private async Task<Quote?> GetEquityQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct)
    {
        var symbol = symbolRouter.RouteSymbol(ticker, assetClass);
        var body = await _api.GetAsync<FinnhubQuoteResponse>(
            $"quote?symbol={Uri.EscapeDataString(symbol)}", ticker, ct);

        if (body is null || body.Current == 0m)
        {
            logger?.LogInformation("Finnhub /quote returned no data for {Ticker}.", ticker);
            return null;
        }

        return new Quote(
            Ticker: ticker.ToUpperInvariant(),
            AssetClass: assetClass,
            Price: body.Current,
            PreviousClose: body.PreviousClose,
            PercentChange: body.PercentChange ?? 0m,
            FetchedAtUtc: clock.UtcNow);
    }

    private async Task<Quote?> GetCryptoQuoteAsync(string ticker, CancellationToken ct)
    {
        var symbol = symbolRouter.RouteSymbol(ticker, AssetClass.Crypto);
        var to = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds();
        var from = to - 5 * 24 * 60 * 60; // 5 days back, plenty for two daily closes
        var body = await _api.GetAsync<FinnhubCryptoCandleResponse>(
            $"crypto/candle?symbol={Uri.EscapeDataString(symbol)}&resolution=D&from={from}&to={to}",
            $"{ticker} ({symbol})", ct);

        if (body is null || body.Status != "ok" || body.Close is null || body.Close.Length < 2)
        {
            logger?.LogInformation("Finnhub /crypto/candle returned insufficient data for {Ticker}.", ticker);
            return null;
        }

        var latest = body.Close[^1];
        var prev = body.Close[^2];
        var pct = prev == 0m ? 0m : ((latest - prev) / prev) * 100m;

        return new Quote(
            Ticker: ticker.ToUpperInvariant(),
            AssetClass: AssetClass.Crypto,
            Price: latest,
            PreviousClose: prev,
            PercentChange: pct,
            FetchedAtUtc: clock.UtcNow);
    }
}
