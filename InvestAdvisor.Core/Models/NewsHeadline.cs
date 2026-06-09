namespace InvestAdvisor.Core.Models;

public sealed record NewsHeadline(
    string? Ticker,
    string Headline,
    string Source,
    string Url,
    DateTime PublishedAtUtc);
