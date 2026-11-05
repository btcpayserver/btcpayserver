#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Logging;
using MimeKit;
using static BTCPayServer.Plugins.Monetization.Views.SelectExistingOfferingModalViewModel;

namespace BTCPayServer.Plugins.Emails.Services;

public abstract class EmailSender(IBackgroundJobClient jobClient, EventAggregator eventAggregator,
    EmailLogRepository emailLogs, Logs logs) : IEmailSender
{
    public EventAggregator EventAggregator { get; } = eventAggregator;
    public Logs Logs { get; } = logs;

    public void SendEmail(MailboxAddress email, string subject, string message)
    {
        SendEmail(new[] { email }, Array.Empty<MailboxAddress>(), Array.Empty<MailboxAddress>(), subject, message);
    }

    public virtual void SendEmail(MailboxAddress[] email, MailboxAddress[] cc, MailboxAddress[] bcc, string subject, string message) => SendEmail(email, cc, bcc, subject, message, null, null);

    public void SendEmail(MailboxAddress[] email, MailboxAddress[] cc, MailboxAddress[] bcc, string subject, string message, string? storeId, string? trigger)
    {
        jobClient.Schedule(async cancellationToken =>
        {
            var log = EmailLogEntry.Create(storeId);
            log.Trigger = trigger ?? "Unknown";
            log.To = Array.ConvertAll(email ?? [], m => m.ToString());
            log.CC = Array.ConvertAll(cc ?? [], m => m.ToString());
            log.BCC = Array.ConvertAll(bcc ?? [], m => m.ToString());
            log.Subject = subject;
            log.Body = message;
            log.Status = EmailLogStatus.Failed;
            try
            {
                var emailSettings = await GetEmailSettings();
                if (emailSettings?.IsComplete() is not true)
                {
                    log.Error = "The email settings are not configured";
                    Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                    return;
                }
                using var smtp = await emailSettings.CreateSmtpClient();
                var mail = emailSettings.CreateMailMessage(email, cc, bcc, subject, message, true);
                var response = await smtp.SendAsync(mail, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);
                log.Status = EmailLogStatus.Sent;
                EventAggregator.Publish(new Events.EmailSentEvent(response, mail));
            }
            catch (Exception ex)
            {
                log.Error = ex.Message;
                Logs.Configuration.LogWarning(ex, "Failed to send email ({Trigger})", trigger);
                throw;
            }
            finally
            {
                try { await emailLogs.Add(log); }
                catch (Exception ex)
                {
                    Logs.Configuration.LogWarning(ex, "Failed to save the email log ({Trigger})", trigger);
                }
            }
        }, TimeSpan.Zero);
    }

    public abstract Task<EmailSettings?> GetEmailSettings();
}
