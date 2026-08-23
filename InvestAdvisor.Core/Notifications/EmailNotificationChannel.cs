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
    ILogger<EmailNotificationChannel>? logger = null) : INotificationChannel
{
    public string ChannelName => "Email";

    public bool ShouldDispatch(AgentAnalysis analysis) => AlertPolicy.IsAlertWorthy(analysis);

    public async Task<DeliveryOutcome> SendAsync(
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
            return new DeliveryOutcome(ChannelName, DeliveryStatus.Skipped,
                "Email channel disabled or missing host/from/to in RuntimeSettings.");
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
            return new DeliveryOutcome(ChannelName, DeliveryStatus.Sent);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SMTP send failed for AdviceLog {Id}.", adviceLog.Id);
            return new DeliveryOutcome(ChannelName, DeliveryStatus.Failed, ex.Message);
        }
    }
}
