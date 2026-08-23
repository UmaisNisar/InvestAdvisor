namespace InvestAdvisor.Core.Options;

/// <summary>
/// Not bound through <c>IOptions</c> — the hosts read <c>Scheduler:WorkerEnabled</c> straight
/// off <c>IConfiguration</c> before the container is built. Tick interval and run caps live in
/// <c>RuntimeSettings</c> so they can be changed from the UI without a restart.
/// </summary>
public static class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>
    /// Whether the background workers that spend LLM credits (agent loop + screener/daily
    /// recommendation + strategy scans) are registered. When unset it defaults to "on outside
    /// Development" so a local <c>dotnet run</c> doesn't burn credits. Set
    /// <c>Scheduler:WorkerEnabled=true</c> to force them on in dev.
    /// </summary>
    public const string WorkerEnabledKey = SectionName + ":WorkerEnabled";
}
