using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface INotificationChannel
{
    string ChannelName { get; }

    bool ShouldDispatch(AgentAnalysis analysis);

    Task<AlertDelivery> SendAsync(
        AdviceLog adviceLog,
        AgentAnalysis analysis,
        CancellationToken ct = default);
}
