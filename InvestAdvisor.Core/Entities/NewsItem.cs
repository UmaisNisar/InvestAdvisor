using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class NewsItem
{
    public long Id { get; set; }
    public string? Ticker { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedAtUtc { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Which channel this came from — curated news, StockTwits, or Reddit.</summary>
    public NewsSource Channel { get; set; } = NewsSource.News;

    /// <summary>LLM-graded sentiment in [-1, +1] (negative = bearish). Null until scored.</summary>
    public decimal? SentimentScore { get; set; }

    /// <summary>Bucketed sentiment: bullish | neutral | bearish. Null until scored.</summary>
    public string? SentimentLabel { get; set; }

    /// <summary>When sentiment was computed. Null marks a row the scorer still needs to process.</summary>
    public DateTime? SentimentScoredAtUtc { get; set; }
}
