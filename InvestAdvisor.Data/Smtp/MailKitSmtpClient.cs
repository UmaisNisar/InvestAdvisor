using InvestAdvisor.Core.Abstractions;
using MailKit.Security;
using MimeKit;

namespace InvestAdvisor.Data.Smtp;

public sealed class MailKitSmtpClient : ISmtpClient
{
    public async Task SendAsync(SmtpMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(message.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder
        {
            TextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody,
        };
        mime.Body = builder.ToMessageBody();

        using var client = new MailKit.Net.Smtp.SmtpClient();
        var secureSocketOptions = message.EnableSsl
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        await client.ConnectAsync(message.Host, message.Port, secureSocketOptions, ct);

        if (!string.IsNullOrEmpty(message.Username) && !string.IsNullOrEmpty(message.Password))
            await client.AuthenticateAsync(message.Username, message.Password, ct);

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
