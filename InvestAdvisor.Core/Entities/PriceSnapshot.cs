using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class PriceSnapshot
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public AssetClass AssetClass { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal PreviousClose { get; set; }
    public decimal PercentChange { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
