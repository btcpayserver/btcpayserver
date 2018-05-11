using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using Hangfire;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Mails
{
    // This class is used by the application to send email for account confirmation and password reset.
    // For more details see https://go.microsoft.com/fwlink/?LinkID=532713
    public class EmailSender : IEmailSender
    {
        IBackgroundJobClient _JobClient;
        SettingsRepository _Repository;
        public EmailSender(IBackgroundJobClient jobClient, SettingsRepository repository)
        {
            if (jobClient == null)
                throw new ArgumentNullException(nameof(jobClient));
            _JobClient = jobClient;
            _Repository = repository;
        }
        public async Task SendEmailAsync(string email, string subject, string message)
        {
            var settings = await _Repository.GetSettingAsync<EmailSettings>() ?? new EmailSettings();
            if (!settings.IsComplete())
            {
                Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                return;
            }
            _JobClient.Schedule(() => SendMailCore(email, subject, message), TimeSpan.Zero);
            return;
        }

        public async Task SendMailCore(string email, string subject, string message)
        {
            var settings = await _Repository.GetSettingAsync<EmailSettings>() ?? new EmailSettings();
            if (!settings.IsComplete())
                throw new InvalidOperationException("Email settings not configured");
            var smtp = settings.CreateSmtpClient();
            MailMessage mail = new MailMessage(settings.From, email, subject, message);
            mail.IsBodyHtml = true;
            await smtp.SendMailAsync(mail);
        }
    }
}
