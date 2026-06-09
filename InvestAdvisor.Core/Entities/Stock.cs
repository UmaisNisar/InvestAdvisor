using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

/// <summary>One member of the screener's tracked universe (equity, ETF, or crypto).</summary>
public class Stock
{
    public int Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public AssetClass AssetClass { get; set; } = AssetClass.Equity;

    /// <summary>Provider-specific id for non-equities (e.g. the CoinGecko coin id for crypto).
    /// Null for equities/ETFs, which are keyed by <see cref="Ticker"/>.</summary>
    public string? ExternalId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}
