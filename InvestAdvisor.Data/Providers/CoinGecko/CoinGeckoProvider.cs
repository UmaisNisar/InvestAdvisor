using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers.CoinGecko;

/// <summary>
/// CoinGecko free-tier markets endpoint. No API key required. One call returns price, market cap,
/// and 7-/30-day price change for every requested coin id. Degrades to empty on failure.
/// </summary>
public sealed class CoinGeckoProvider(
    HttpClient http,
    ILogger<CoinGeckoProvider>? logger = null) : ICryptoMarketProvider
{
    public async Task<IReadOnlyList<CryptoMarket>> GetMarketsAsync(
        IReadOnlyCollection<string> coinIds, CancellationToken ct = default)
    {
        if (coinIds.Count == 0) return Array.Empty<CryptoMarket>();

        var ids = string.Join(",", coinIds);
        var url = $"/api/v3/coins/markets?vs_currency=usd&ids={Uri.EscapeDataString(ids)}" +
                  "&price_change_percentage=7d,30d&order=market_cap_desc&per_page=250&sparkline=false";

        CoinRow[]? rows;
        try { rows = await http.GetFromJsonAsync<CoinRow[]>(url, ct); }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "CoinGecko markets fetch failed.");
            return Array.Empty<CryptoMarket>();
        }

        if (rows is null) return Array.Empty<CryptoMarket>();
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .Select(r => new CryptoMarket(
                CoinId: r.Id!,
                Symbol: (r.Symbol ?? string.Empty).ToUpperInvariant(),
                Price: r.CurrentPrice,
                MarketCap: r.MarketCap,
                Return7d: r.PriceChangePercentage7d,
                Return30d: r.PriceChangePercentage30d,
                Change24hPct: r.PriceChangePercentage24h))
            .ToList();
    }

    private sealed record CoinRow(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("current_price")] decimal? CurrentPrice,
        [property: JsonPropertyName("market_cap")] decimal? MarketCap,
        [property: JsonPropertyName("price_change_percentage_24h")] decimal? PriceChangePercentage24h,
        [property: JsonPropertyName("price_change_percentage_7d_in_currency")] decimal? PriceChangePercentage7d,
        [property: JsonPropertyName("price_change_percentage_30d_in_currency")] decimal? PriceChangePercentage30d);
}
