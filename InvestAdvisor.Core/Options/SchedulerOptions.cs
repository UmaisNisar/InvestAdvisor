namespace InvestAdvisor.Core.Options;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>Default tick interval used until <c>RuntimeSettings</c> overrides it.</summary>
    public int DefaultTickIntervalSeconds { get; set; } = 300;

    /// <summary>Safety cap on agent runs per day, independent of RuntimeSettings override.</summary>
    public int HardMaxRunsPerDay { get; set; } = 96;
}
