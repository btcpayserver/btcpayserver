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
        public Task SendEmailAsync(string email, string subject, string message)
        {
            _JobClient.Schedule(() => SendMailCore(email, subject, message), TimeSpan.Zero);
            return Task.CompletedTask;
        }

        public async Task SendMailCore(string email, string subject, string message)
        {
            var settings = await _Repository.GetSettingAsync<EmailSettings>();
            if (settings == null)
                throw new InvalidOperationException("Email settings not configured");
            var smtp = settings.CreateSmtpClient();
            MailMessage mail = new MailMessage(settings.From, email, subject, message);
            mail.IsBodyHtml = true;
            await smtp.SendMailAsync(mail);
        }
    }
}
