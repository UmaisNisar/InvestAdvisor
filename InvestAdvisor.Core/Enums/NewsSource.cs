namespace InvestAdvisor.Core.Enums;

/// <summary>
/// Which channel a <see cref="Entities.NewsItem"/> came from. The sentiment aggregator can weight
/// these differently (curated news vs. retail social chatter).
/// </summary>
public enum NewsSource
{
    News = 0,
    StockTwits = 1,
    Reddit = 2,
    Bluesky = 3,
    HackerNews = 4,
}
