using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A closed (sold) position lot — the permanent record of a realized gain/loss. Holdings only model
/// what is currently owned; when a position is sold the holdings CSV simply stops mentioning it, so
/// realized P&amp;L would otherwise be lost. These rows are created by importing a Wealthsimple Activity
/// export (the authoritative sell record), or entered by hand. Realized P&amp;L = <see cref="Proceeds"/>
/// − <see cref="CostBasis"/>, both in <see cref="Currency"/>.
/// </summary>
public class RealizedLot
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetClass AssetClass { get; set; }
    public AccountType AccountType { get; set; }
    public decimal Quantity { get; set; }          // shares sold in this event
    public decimal Proceeds { get; set; }          // net sale proceeds, in Currency
    public decimal CostBasis { get; set; }         // cost of the shares sold, in Currency
    /// <summary>Currency the Proceeds/CostBasis are denominated in (e.g. "USD", "CAD").</summary>
    public string Currency { get; set; } = "USD";
    public DateTime RealizedAtUtc { get; set; }    // trade fill date
    /// <summary>Stable dedup key for imported rows (tenant|date|ticker|account|qty|proceeds); empty for manual entries.</summary>
    public string SourceHash { get; set; } = string.Empty;
    public bool ManualEntry { get; set; }          // true when hand-entered rather than imported
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
