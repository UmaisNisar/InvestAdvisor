using System.Net.Http.Json;
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
    private readonly FinnhubOptions _opts = options.Value;

    public async Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "Finnhub API key not configured. Set Finnhub:ApiKey via user-secrets or the FINNHUB_API_KEY env var.");

        await rateLimiter.WaitAsync(ct);
        return assetClass == AssetClass.Crypto
            ? await GetCryptoQuoteAsync(ticker, ct)
            : await GetEquityQuoteAsync(ticker, assetClass, ct);
    }

    private async Task<Quote?> GetEquityQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct)
    {
        var symbol = symbolRouter.RouteSymbol(ticker, assetClass);
        var url = $"/api/v1/quote?symbol={Uri.EscapeDataString(symbol)}&token={Uri.EscapeDataString(_opts.ApiKey)}";

        FinnhubQuoteResponse? body;
        try
        {
            body = await http.GetFromJsonAsync<FinnhubQuoteResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Finnhub /quote failed for {Ticker}.", ticker);
            return null;
        }

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
        var url = $"/api/v1/crypto/candle?symbol={Uri.EscapeDataString(symbol)}" +
                  $"&resolution=D&from={from}&to={to}&token={Uri.EscapeDataString(_opts.ApiKey)}";

        FinnhubCryptoCandleResponse? body;
        try
        {
            body = await http.GetFromJsonAsync<FinnhubCryptoCandleResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Finnhub /crypto/candle failed for {Ticker} ({Symbol}).", ticker, symbol);
            return null;
        }

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
