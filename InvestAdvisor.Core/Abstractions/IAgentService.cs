using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IAgentService
{
    /// <summary>
    /// Runs the agent once for the given trigger: assembles context, calls Anthropic,
    /// persists the AdviceLog row, and returns its id.
    /// </summary>
    Task<long> RunAsync(RunTrigger trigger, CancellationToken ct = default);

    /// <summary>
    /// Runs immediately as <see cref="Enums.RunTriggerKind.Manual"/>. The worker UI uses this.
    /// </summary>
    Task<long> RunNowAsync(string? note, CancellationToken ct = default);

    /// <summary>
    /// "Re-run with this prompt": loads the original AdviceLog's StructuredInputJson and
    /// invokes Anthropic against the edited prompt. Inserts a new AdviceLog with
    /// <c>ReplayOfAdviceLogId = sourceAdviceLogId</c>.
    /// </summary>
    Task<long> ReplayAsync(long sourceAdviceLogId, string systemPrompt, CancellationToken ct = default);
}
