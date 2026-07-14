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
    ApplicationDbContextFactory dbContextFactory, Logs logs) : IEmailSender
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
            var emailSettings = await GetEmailSettings();
            if (emailSettings?.IsComplete() is not true)
            {
                Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                return;
            }

            var log = EmailLogData.Create(storeId);
            var blob = new EmailLogBlob
            {
                Trigger = trigger ?? "Unknown",
                To = Array.ConvertAll(email ?? [], m => m.ToString()),
                CC = Array.ConvertAll(cc ?? [], m => m.ToString()),
                BCC = Array.ConvertAll(bcc ?? [], m => m.ToString()),
                Subject = subject,
                Body = message
            };

            try
            {
                using var smtp = await emailSettings.CreateSmtpClient();
                var mail = emailSettings.CreateMailMessage(email, cc, bcc, subject, message, true);
                var response = await smtp.SendAsync(mail, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);

                blob.Status = EmailLogStatus.Sent;
                EventAggregator.Publish(new Events.EmailSentEvent(response, mail));
            }
            catch (Exception ex)
            {
                blob.Status = EmailLogStatus.Failed;
                blob.Error = ex.Message;
                Logs.Configuration.LogWarning(ex, "Failed to send email ({Trigger})", trigger);
                throw;
            }
            finally
            {
                log.SetBlob(blob);
                await using var ctx = dbContextFactory.CreateContext();
                ctx.EmailLogs.Add(log);
                await ctx.SaveChangesAsync(CancellationToken.None);
            }
        }, TimeSpan.Zero);
    }

    public abstract Task<EmailSettings?> GetEmailSettings();
}
