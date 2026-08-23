using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Notifications;

namespace InvestAdvisor.Core.Abstractions;

public interface INotificationChannel
{
    string ChannelName { get; }

    bool ShouldDispatch(AgentAnalysis analysis);

    Task<DeliveryOutcome> SendAsync(
        AdviceLog adviceLog,
        AgentAnalysis analysis,
        CancellationToken ct = default);
}
