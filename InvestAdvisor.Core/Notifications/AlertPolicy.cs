using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Notifications;

/// <summary>
/// The single rule for "is this run worth interrupting the user about". Every
/// <see cref="Abstractions.INotificationChannel"/> gates on it so email and the in-app bell
/// can never disagree.
/// </summary>
public static class AlertPolicy
{
    public static bool IsAlertWorthy(AgentAnalysis analysis)
    {
        var hasFlag = analysis.Flags.Any(f => f.Severity >= FlagSeverity.Warn);
        var hasDrift = analysis.DriftAlerts.Any(d => d.Severity == DriftSeverity.ActionSuggested);
        return hasFlag || hasDrift;
    }
}
