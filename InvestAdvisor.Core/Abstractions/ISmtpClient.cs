namespace InvestAdvisor.Core.Abstractions;

/// <summary>Thin SMTP abstraction so EmailNotificationChannel is unit-testable.</summary>
public interface ISmtpClient
{
    Task SendAsync(SmtpMessage message, CancellationToken ct = default);
}

public sealed record SmtpMessage(
    string Host,
    int Port,
    bool EnableSsl,
    string? Username,
    string? Password,
    string From,
    string To,
    string Subject,
    string HtmlBody,
    string PlainTextBody);
