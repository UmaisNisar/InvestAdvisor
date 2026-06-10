using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// A social-media source of ticker-tagged posts (StockTwits, Reddit, …). Each provider declares its
/// <see cref="Channel"/> so persisted rows can be attributed and weighted. Implementations degrade to
/// an empty list on any failure rather than throwing, so one flaky source never stalls a refresh.
/// </summary>
public interface ISocialFeedProvider
{
    /// <summary>Which channel this provider feeds.</summary>
    NewsSource Channel { get; }

    /// <summary>Recent posts mentioning <paramref name="ticker"/>; provider decides the lookback window.</summary>
    Task<IReadOnlyList<SocialPost>> GetTickerPostsAsync(string ticker, CancellationToken ct = default);
}

/// <summary>
/// One social post, normalized across sources. <paramref name="ProviderSentiment"/> is the source's own
/// tag (e.g. StockTwits "Bullish"/"Bearish") when present — kept as a cheap prior, but the LLM scorer
/// remains the source of truth so every channel lands on one scale.
/// </summary>
public sealed record SocialPost(
    string Ticker,
    string Text,
    string Source,
    string Url,
    DateTime CreatedAtUtc,
    NewsSource Channel,
    string? ProviderSentiment = null);
