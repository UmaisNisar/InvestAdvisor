using System.Net.Http.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers.Finnhub;

/// <summary>
/// The request prologue every Finnhub call shares: the API-key guard, the rate-limiter wait,
/// the <c>token</c> query parameter, and the "log a warning and return nothing" failure path.
/// Each Finnhub provider wraps its own typed <see cref="HttpClient"/> in one of these.
/// </summary>
public sealed class FinnhubApi(HttpClient http, IRateLimiter rateLimiter, FinnhubOptions opts, ILogger? logger)
{
    public const string MissingKeyMessage =
        "Finnhub API key not configured. Set Finnhub:ApiKey via user-secrets or the FINNHUB_API_KEY env var.";

    /// <summary>Throws when no key is configured — callers surface this as a setup error, not a fetch failure.</summary>
    public void EnsureKey()
    {
        if (string.IsNullOrWhiteSpace(opts.ApiKey)) throw new InvalidOperationException(MissingKeyMessage);
    }

    /// <summary>Rate-limited JSON GET of <c>/api/v1/{pathAndQuery}&amp;token=…</c>; null (logged) on any failure.</summary>
    public async Task<T?> GetAsync<T>(string pathAndQuery, string label, CancellationToken ct, bool waitRateLimit = true)
        where T : class
    {
        EnsureKey();
        if (waitRateLimit) await rateLimiter.WaitAsync(ct);
        try
        {
            return await http.GetFromJsonAsync<T>(Url(pathAndQuery), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Finnhub {Endpoint} failed for {Label}.", Endpoint(pathAndQuery), label);
            return null;
        }
    }

    /// <summary>Rate-limited raw GET for endpoints parsed by hand; null (logged) on any failure.</summary>
    public async Task<string?> GetStringAsync(string pathAndQuery, string label, CancellationToken ct)
    {
        EnsureKey();
        await rateLimiter.WaitAsync(ct);
        try
        {
            return await http.GetStringAsync(Url(pathAndQuery), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Finnhub {Endpoint} failed for {Label}.", Endpoint(pathAndQuery), label);
            return null;
        }
    }

    private string Url(string pathAndQuery) =>
        $"/api/v1/{pathAndQuery}{(pathAndQuery.Contains('?') ? "&" : "?")}token={Uri.EscapeDataString(opts.ApiKey)}";

    private static string Endpoint(string pathAndQuery) => "/" + pathAndQuery.Split('?')[0];
}
