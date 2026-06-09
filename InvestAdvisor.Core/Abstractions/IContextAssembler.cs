using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IContextAssembler
{
    Task<RunContext> BuildAsync(RunTrigger trigger, CancellationToken ct = default);
}
