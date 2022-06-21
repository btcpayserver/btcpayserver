using System;
using System.Linq;
using System.Net.Mail;
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

        public void SendEmail(MailAddress email, string subject, string message)
        {
            SendEmail(new[] {email}, Array.Empty<MailAddress>(), Array.Empty<MailAddress>(), subject, message);
        }

        public void SendEmail(MailAddress[] email, MailAddress[] cc, MailAddress[] bcc, string subject, string message)
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
                var mail = emailSettings.CreateMailMessage(email.Select(ToMailboxAddress).ToArray(),
                    cc.Select(ToMailboxAddress).ToArray(), bcc.Select(ToMailboxAddress).ToArray(),
                    subject, message, true);
                await smtp.SendAsync(mail, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);
            }, TimeSpan.Zero);
        }

        public abstract Task<EmailSettings> GetEmailSettings();

        private MailboxAddress ToMailboxAddress(MailAddress a) => new (a.ToString(), a.ToString());
    }
}
