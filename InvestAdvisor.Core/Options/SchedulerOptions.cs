namespace InvestAdvisor.Core.Options;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>Default tick interval used until <c>RuntimeSettings</c> overrides it.</summary>
    public int DefaultTickIntervalSeconds { get; set; } = 300;

    /// <summary>Safety cap on agent runs per day, independent of RuntimeSettings override.</summary>
    public int HardMaxRunsPerDay { get; set; } = 96;

    /// <summary>
    /// Whether the background workers that spend Anthropic credits (agent loop + screener/daily
    /// recommendation) are registered. Read directly in the host; when unset it defaults to
    /// "on outside Development" so a local <c>dotnet run</c> doesn't burn credits. Set
    /// <c>Scheduler:WorkerEnabled=true</c> to force them on in dev.
    /// </summary>
    public bool WorkerEnabled { get; set; } = true;
}
