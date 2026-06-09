using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class WatchlistItem
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public AssetClass AssetClass { get; set; }
    public string? Note { get; set; }
    public decimal? PriceTargetLow { get; set; }
    public decimal? PriceTargetHigh { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
