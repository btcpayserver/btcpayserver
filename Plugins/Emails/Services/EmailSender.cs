#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Plugins.Emails.Services;

public abstract class EmailSender(IBackgroundJobClient jobClient, EventAggregator eventAggregator, Logs logs) : IEmailSender
{
    public EventAggregator EventAggregator { get; } = eventAggregator;
    public Logs Logs { get; } = logs;

    public void SendEmail(MailboxAddress email, string subject, string message)
    {
        SendEmail(new[] { email }, Array.Empty<MailboxAddress>(), Array.Empty<MailboxAddress>(), subject, message);
    }

    public void SendEmail(MailboxAddress[] email, MailboxAddress[] cc, MailboxAddress[] bcc, string subject, string message)
    {
        jobClient.Schedule(async cancellationToken =>
        {
            var emailSettings = await GetEmailSettings();
            if (emailSettings?.IsComplete() is not true)
            {
                Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                return;
            }

            using var smtp = await emailSettings.CreateSmtpClient();
            var mail = emailSettings.CreateMailMessage(email, cc, bcc, subject, message, true);
            var response = await smtp.SendAsync(mail, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);
            EventAggregator.Publish(new Events.EmailSentEvent(response, mail));
        }, TimeSpan.Zero);
    }

    public abstract Task<EmailSettings?> GetEmailSettings();
}
