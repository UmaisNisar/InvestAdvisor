using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Symbol type-ahead. First matches the curated universe (instant, known asset class), then tops
/// up with Finnhub's <c>/search</c> for anything else. Degrades to local-only on API failure.
/// </summary>
public sealed class TickerSearchService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IHttpClientFactory httpFactory,
    IRateLimiter rateLimiter,
    IOptions<FinnhubOptions> options,
    ILogger<TickerSearchService>? logger = null) : ITickerSearchService
{
    private readonly FinnhubOptions _opts = options.Value;
    private const int MaxResults = 12;

    public async Task<IReadOnlyList<TickerSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length < 1) return Array.Empty<TickerSearchResult>();

        var results = new List<TickerSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Curated universe — instant, and carries a known asset class.
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var q = query.ToUpperInvariant();
            var local = await db.Stocks.AsNoTracking()
                .Where(s => s.Ticker.ToUpper().Contains(q) || s.Name.ToUpper().Contains(q))
                .OrderBy(s => s.Ticker)
                .Take(MaxResults)
                .Select(s => new { s.Ticker, s.Name, s.AssetClass })
                .ToListAsync(ct);
            foreach (var s in local)
                if (seen.Add(s.Ticker))
                    results.Add(new TickerSearchResult(s.Ticker, s.Name, s.AssetClass));
        }

        // 2) Finnhub broad search (best-effort; mostly stocks/ETFs).
        if (results.Count < MaxResults && !string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            try
            {
                await rateLimiter.WaitAsync(ct);
                var url = $"{_opts.BaseUrl}/search?q={Uri.EscapeDataString(query)}&token={Uri.EscapeDataString(_opts.ApiKey)}";
                var client = httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var resp = await client.GetFromJsonAsync<FinnhubSearchResponse>(url, ct);
                foreach (var r in resp?.Result ?? Array.Empty<FinnhubSearchRow>())
                {
                    if (results.Count >= MaxResults) break;
                    var sym = r.Symbol?.Trim();
                    if (string.IsNullOrWhiteSpace(sym)) continue;
                    // Allow US symbols + Canadian (.TO / .V) + Australian (.AX); skip other foreign-exchange suffixes.
                    if (sym.Contains('.')
                        && !sym.EndsWith(".TO", StringComparison.OrdinalIgnoreCase)
                        && !sym.EndsWith(".V", StringComparison.OrdinalIgnoreCase)
                        && !sym.EndsWith(".AX", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(sym)) continue;
                    results.Add(new TickerSearchResult(sym, r.Description ?? sym, MapType(r.Type)));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger?.LogDebug(ex, "Finnhub symbol search failed for '{Query}' (using local results only).", query); }
        }

        return results;
    }

    private static AssetClass MapType(string? type)
    {
        var t = (type ?? string.Empty).ToLowerInvariant();
        if (t.Contains("etf") || t.Contains("etp") || t.Contains("fund")) return AssetClass.Etf;
        if (t.Contains("crypto")) return AssetClass.Crypto;
        return AssetClass.Equity;
    }

    private sealed record FinnhubSearchResponse(
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("result")] FinnhubSearchRow[]? Result);

    private sealed record FinnhubSearchRow(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("type")] string? Type);
}
