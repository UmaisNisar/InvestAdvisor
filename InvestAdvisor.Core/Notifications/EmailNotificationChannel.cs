using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Core.Notifications;

public sealed class EmailNotificationChannel(
    IRuntimeSettingsStore settingsStore,
    IOptions<SmtpOptions> smtpOptions,
    ISmtpClient smtp,
    ISystemClock clock,
    ILogger<EmailNotificationChannel>? logger = null) : INotificationChannel
{
    public string ChannelName => "Email";

    public bool ShouldDispatch(AgentAnalysis analysis)
    {
        var hasFlag = analysis.Flags.Any(f => f.Severity >= FlagSeverity.Warn);
        var hasDrift = analysis.DriftAlerts.Any(d => d.Severity == DriftSeverity.ActionSuggested);
        return hasFlag || hasDrift;
    }

    public async Task<AlertDelivery> SendAsync(
        AdviceLog adviceLog,
        AgentAnalysis analysis,
        CancellationToken ct = default)
    {
        var settings = await settingsStore.GetAsync(ct);
        if (!settings.EmailEnabled
            || string.IsNullOrWhiteSpace(settings.SmtpHost)
            || string.IsNullOrWhiteSpace(settings.SmtpFrom)
            || string.IsNullOrWhiteSpace(settings.SmtpTo))
        {
            return new AlertDelivery
            {
                AdviceLogId = adviceLog.Id,
                Channel = ChannelName,
                Status = DeliveryStatus.Skipped,
                ErrorMessage = "Email channel disabled or missing host/from/to in RuntimeSettings.",
                AttemptCount = 1,
            };
        }

        var (html, plain) = DigestRenderer.BuildBody(adviceLog, analysis);
        var subject = DigestRenderer.BuildSubject(adviceLog);

        var msg = new SmtpMessage(
            Host: settings.SmtpHost!,
            Port: settings.SmtpPort,
            EnableSsl: settings.SmtpEnableSsl,
            Username: settings.SmtpFrom,
            Password: smtpOptions.Value.Password,
            From: settings.SmtpFrom!,
            To: settings.SmtpTo!,
            Subject: subject,
            HtmlBody: html,
            PlainTextBody: plain);

        try
        {
            await smtp.SendAsync(msg, ct);
            return new AlertDelivery
            {
                AdviceLogId = adviceLog.Id,
                Channel = ChannelName,
                Status = DeliveryStatus.Sent,
                DeliveredAtUtc = clock.UtcNow,
                AttemptCount = 1,
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SMTP send failed for AdviceLog {Id}.", adviceLog.Id);
            return new AlertDelivery
            {
                AdviceLogId = adviceLog.Id,
                Channel = ChannelName,
                Status = DeliveryStatus.Failed,
                ErrorMessage = ex.Message,
                AttemptCount = 1,
            };
        }
    }
}
