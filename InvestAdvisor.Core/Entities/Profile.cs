using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class Profile
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string GoalsText { get; set; } = string.Empty;
    public RiskTolerance RiskTolerance { get; set; } = RiskTolerance.Moderate;
    public TimeHorizon TimeHorizon { get; set; } = TimeHorizon.LongTerm;
    public decimal DriftPctThreshold { get; set; } = 5m;
    public decimal SingleDayMovePctThreshold { get; set; } = 7m;
    public int RebalanceCadenceHours { get; set; } = 24;
    public string? SystemPromptOverride { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
