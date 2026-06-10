using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IContextAssembler
{
    Task<RunContext> BuildAsync(int tenantId, RunTrigger trigger, CancellationToken ct = default);
}
