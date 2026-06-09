using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.RateLimiting;

/// <summary>
/// A simple monotonic token bucket. Refills <c>RequestsPerMinute</c> tokens per minute,
/// capped at the same value as the burst size. Sized for Finnhub's 60 req/min free tier;
/// safe to share across the typed Finnhub HttpClient and the news HttpClient since both
/// debit the same provider quota.
/// </summary>
public sealed class TokenBucketRateLimiter : IRateLimiter, IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly double _refillTokensPerSecond;
    private double _tokens;
    private DateTime _lastRefillUtc;
    private bool _disposed;

    public TokenBucketRateLimiter(IOptions<FinnhubOptions> options)
    {
        var perMinute = Math.Max(1, options.Value.RequestsPerMinute);
        // Sustain ~perMinute/min, but cap the BURST small: starting with a full bucket let the
        // screener fire ~55 calls in one second, which trips Finnhub's per-second limit (429) and
        // then skips the rest of the universe. A small burst paces calls from the very first one.
        _capacity = Math.Min(perMinute, 5);
        _refillTokensPerSecond = perMinute / 60.0;
        _tokens = _capacity;
        _lastRefillUtc = DateTime.UtcNow;
    }

    public async ValueTask WaitAsync(CancellationToken ct = default)
    {
        while (true)
        {
            TimeSpan? waitFor;
            lock (_gate)
            {
                Refill();
                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return;
                }
                var deficitTokens = 1.0 - _tokens;
                var seconds = deficitTokens / _refillTokensPerSecond;
                waitFor = TimeSpan.FromSeconds(Math.Min(seconds, 60));
            }
            await Task.Delay(waitFor.Value, ct);
        }
    }

    private void Refill()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefillUtc).TotalSeconds;
        if (elapsed <= 0) return;
        _tokens = Math.Min(_capacity, _tokens + elapsed * _refillTokensPerSecond);
        _lastRefillUtc = now;
    }

    public void Dispose() => _disposed = true;
}
