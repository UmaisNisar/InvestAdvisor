using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class Holding
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetClass AssetClass { get; set; }
    public decimal Quantity { get; set; }
    public decimal AvgCost { get; set; }
    /// <summary>Currency the price/cost are denominated in (e.g. "USD", "CAD"). Converted to USD for totals.</summary>
    public string Currency { get; set; } = "USD";
    public AccountType AccountType { get; set; }
    public decimal? TargetAllocationPct { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
