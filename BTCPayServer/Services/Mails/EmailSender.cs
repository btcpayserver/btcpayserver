using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Services.Mails
{
    public abstract class EmailSender : IEmailSender
    {
        public Logs Logs { get; }

        readonly IBackgroundJobClient _JobClient;

        public EmailSender(IBackgroundJobClient jobClient, Logs logs)
        {
            Logs = logs;
            _JobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        }

        public void SendEmail(MailboxAddress email, string subject, string message)
        {
            SendEmail(new[] { email }, Array.Empty<MailboxAddress>(), Array.Empty<MailboxAddress>(), subject, message);
        }

        public void SendEmail(MailboxAddress[] email, MailboxAddress[] cc, MailboxAddress[] bcc, string subject, string message)
        {
            _JobClient.Schedule(async (cancellationToken) =>
            {
                var emailSettings = await GetEmailSettings();
                if (emailSettings?.IsComplete() != true)
                {
                    Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                    return;
                }

                using var smtp = await emailSettings.CreateSmtpClient();
                var mail = emailSettings.CreateMailMessage(email, cc, bcc, subject, message, true);
                await smtp.SendAsync(mail, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);
            }, TimeSpan.Zero);
        }

        public abstract Task<EmailSettings> GetEmailSettings();
    }
}
