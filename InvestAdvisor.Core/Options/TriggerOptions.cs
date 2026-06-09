namespace InvestAdvisor.Core.Options;

public sealed class TriggerOptions
{
    public const string SectionName = "Triggers";

    public decimal DefaultDriftPctThreshold { get; set; } = 5m;
    public decimal DefaultSingleDayMovePctThreshold { get; set; } = 7m;
    public int DefaultMinSecondsBetweenRuns { get; set; } = 900;
    public int DefaultMaxSnapshotAgeForTriggerSeconds { get; set; } = 600;
}
