using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers.Fx;

/// <summary>
/// FX rates from Frankfurter (frankfurter.dev) — free, no API key, ECB daily rates. Caches the
/// USD-base table for a few hours and degrades to 1:1 if the fetch fails. Singleton.
/// </summary>
public sealed class FxRateProvider(
    IHttpClientFactory httpFactory,
    ILogger<FxRateProvider>? logger = null) : IFxRateProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, decimal> _usdToX = new(StringComparer.OrdinalIgnoreCase); // units of X per 1 USD
    private DateTime _fetchedUtc = DateTime.MinValue;

    public async Task<decimal> GetRateToUsdAsync(string currency, CancellationToken ct = default)
    {
        currency = (currency ?? "USD").Trim().ToUpperInvariant();
        if (currency is "USD" or "") return 1m;

        await EnsureFreshAsync(ct);
        // _usdToX[CAD] = how many CAD per 1 USD, so CAD→USD = 1 / that.
        return _usdToX.TryGetValue(currency, out var usdToX) && usdToX > 0m ? 1m / usdToX : 1m;
    }

    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        if (_usdToX.Count > 0 && DateTime.UtcNow - _fetchedUtc < Ttl) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_usdToX.Count > 0 && DateTime.UtcNow - _fetchedUtc < Ttl) return;
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.GetFromJsonAsync<FrankfurterResponse>(
                "https://api.frankfurter.dev/v1/latest?base=USD&symbols=CAD,EUR,GBP,AUD,JPY,CHF,HKD,SGD,INR,MXN,NZD", ct);
            if (resp?.Rates is { Count: > 0 })
            {
                _usdToX = new Dictionary<string, decimal>(resp.Rates, StringComparer.OrdinalIgnoreCase);
                _fetchedUtc = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "FX rate fetch failed; using 1:1 fallback."); }
        finally { _gate.Release(); }
    }

    private sealed record FrankfurterResponse(
        [property: JsonPropertyName("base")] string? Base,
        [property: JsonPropertyName("rates")] Dictionary<string, decimal>? Rates);
}
