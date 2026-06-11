using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class Profile
{
    public int Id { get; set; }
    /// <summary>Owning tenant. One profile per tenant.</summary>
    public int TenantId { get; set; }
    public string GoalsText { get; set; } = string.Empty;
    public RiskTolerance RiskTolerance { get; set; } = RiskTolerance.Moderate;
    public TimeHorizon TimeHorizon { get; set; } = TimeHorizon.LongTerm;
    public decimal DriftPctThreshold { get; set; } = 5m;
    public decimal SingleDayMovePctThreshold { get; set; } = 7m;
    public int RebalanceCadenceHours { get; set; } = 24;
    /// <summary>ISO currency the dashboard displays totals in (per-holding prices stay native).</summary>
    public string DisplayCurrency { get; set; } = "USD";
    public string? SystemPromptOverride { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
