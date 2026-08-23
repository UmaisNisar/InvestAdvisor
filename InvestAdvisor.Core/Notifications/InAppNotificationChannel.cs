using InvestAdvisor.Core.Text;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Notifications;

/// <summary>
/// Writes worker-raised alerts into the in-app notification bell. Plugs into the same channel
/// pipeline as <see cref="EmailNotificationChannel"/>, so an automatic drift / price-target / big-move
/// alert lands in the bell (and still emails). Gated identically to email so the two stay in sync.
/// </summary>
public sealed class InAppNotificationChannel(INotificationCenter center) : INotificationChannel
{
    public string ChannelName => "InApp";

    public bool ShouldDispatch(AgentAnalysis analysis) => AlertPolicy.IsAlertWorthy(analysis);

    public async Task<DeliveryOutcome> SendAsync(
        AdviceLog adviceLog,
        AgentAnalysis analysis,
        CancellationToken ct = default)
    {
        var severity = MapSeverity(analysis);
        var body = string.IsNullOrWhiteSpace(analysis.Summary)
            ? "New advice is ready — open to review."
            : Strings.Ellipsize(analysis.Summary, 280);

        await center.AddAsync(adviceLog.TenantId, new NotificationDraft(
            Title: BuildTitle(adviceLog, analysis),
            Body: body,
            Severity: severity,
            LinkUrl: "/advice",
            AdviceLogId: adviceLog.Id), ct);

        return new DeliveryOutcome(ChannelName, DeliveryStatus.Sent);
    }

    private static string BuildTitle(AdviceLog adviceLog, AgentAnalysis analysis)
    {
        var critical = analysis.Flags.Count(f => f.Severity == FlagSeverity.Critical);
        if (critical > 0) return $"{critical} critical flag{(critical == 1 ? "" : "s")} on your portfolio";
        var drift = analysis.DriftAlerts.Count(d => d.Severity == DriftSeverity.ActionSuggested);
        if (drift > 0) return $"{drift} holding{(drift == 1 ? "" : "s")} drifted — action suggested";
        return adviceLog.Trigger switch
        {
            RunTriggerKind.BigMove => "Big move in a holding",
            RunTriggerKind.PriceTarget => "A price target was hit",
            RunTriggerKind.DriftThreshold => "Allocation drift detected",
            _ => "New portfolio advice",
        };
    }

    private static NotificationSeverity MapSeverity(AgentAnalysis analysis)
    {
        if (analysis.Flags.Any(f => f.Severity == FlagSeverity.Critical)) return NotificationSeverity.Error;
        if (analysis.Flags.Any(f => f.Severity == FlagSeverity.Warn)
            || analysis.DriftAlerts.Any(d => d.Severity == DriftSeverity.ActionSuggested))
            return NotificationSeverity.Warning;
        return NotificationSeverity.Info;
    }

}
