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
}
