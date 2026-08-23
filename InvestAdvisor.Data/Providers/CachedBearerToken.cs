using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers;

/// <summary>
/// Double-checked, clock-driven cache for a bearer token that has to be minted from credentials
/// (Reddit client-credentials grant, Bluesky app-password session). <paramref name="acquire"/>
/// returns the token and how long it is good for, or null when the request was refused; any
/// exception it throws is logged and treated as "no token" so the caller degrades gracefully.
/// </summary>
public sealed class CachedBearerToken(
    ISystemClock clock,
    Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>> acquire,
    ILogger? logger,
    string label)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiresUtc = DateTime.MinValue;

    public async Task<string?> GetAsync(CancellationToken ct)
    {
        if (_token is not null && clock.UtcNow < _expiresUtc) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.UtcNow < _expiresUtc) return _token;

            var result = await acquire(ct);
            if (result is not { } r || string.IsNullOrWhiteSpace(r.Token)) return null;

            _token = r.Token;
            _expiresUtc = clock.UtcNow.Add(r.Ttl);
            return _token;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "{Source} token request failed.", label);
            return null;
        }
        finally { _lock.Release(); }
    }
}
