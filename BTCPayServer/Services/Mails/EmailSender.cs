using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Mail;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Mails
{
    public abstract class EmailSender : IEmailSender
    {
        IBackgroundJobClient _JobClient;

        public EmailSender(IBackgroundJobClient jobClient)
        {
            _JobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        }

        public void SendEmail(string email, string subject, string message)
        {
            _JobClient.Schedule(async () =>
            {
                var emailSettings = await GetEmailSettings();
                if (emailSettings?.IsComplete() != true)
                {
                    Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                    return;
                }
                var smtp = emailSettings.CreateSmtpClient();
                var mail = new MailMessage(emailSettings.From, email, subject, message)
                {
                    IsBodyHtml = true
                };
                await smtp.SendMailAsync(mail);

           }, TimeSpan.Zero);
        }

        public abstract Task<EmailSettings> GetEmailSettings();
    }
}
