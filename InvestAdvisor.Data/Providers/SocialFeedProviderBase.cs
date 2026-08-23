using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers;

/// <summary>
/// The contract every social source shares: skip when disabled or given a blank ticker, normalise
/// the symbol, and degrade to an empty list on any failure (logged) so one flaky source never
/// stalls a refresh. Subclasses implement only <see cref="FetchAsync"/> for their API.
/// </summary>
public abstract class SocialFeedProviderBase(ILogger? logger, string label) : ISocialFeedProvider
{
    public abstract NewsSource Channel { get; }

    /// <summary>False when the source is switched off or lacks the credentials it needs.</summary>
    protected abstract bool Enabled { get; }

    /// <summary>Fetch + map for one upper-cased symbol. May throw — the base turns that into an empty result.</summary>
    protected abstract Task<IReadOnlyList<SocialPost>> FetchAsync(string symbol, CancellationToken ct);

    public async Task<IReadOnlyList<SocialPost>> GetTickerPostsAsync(string ticker, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(ticker)) return Array.Empty<SocialPost>();
        var symbol = ticker.Trim().ToUpperInvariant();
        try
        {
            return await FetchAsync(symbol, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "{Source} fetch failed for {Ticker}.", label, symbol);
            return Array.Empty<SocialPost>();
        }
    }

    /// <summary>Logs a non-success status and returns empty — the common "API said no" path.</summary>
    protected IReadOnlyList<SocialPost> EmptyForStatus(string symbol, System.Net.HttpStatusCode status)
    {
        logger?.LogWarning("{Source} request for {Ticker} returned {Status}.", label, symbol, status);
        return Array.Empty<SocialPost>();
    }
}
