namespace InvestAdvisor.Core.Models;

public sealed record NewsHeadline(
    string? Ticker,
    string Headline,
    string Source,
    string Url,
    DateTime PublishedAtUtc,
    decimal? SentimentScore = null,
    string? SentimentLabel = null);

/// <summary>Per-ticker sentiment digest sent to the LLM: mean score in [-1, 1] over recent items.</summary>
public sealed record TickerSentimentView(string Ticker, decimal MeanScore, int PostCount, string Label);
