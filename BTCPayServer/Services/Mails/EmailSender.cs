using BTCPayServer.Logging;
using NBitcoin;
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
            _JobClient.Schedule(async (cancellationToken) =>
            {
                var emailSettings = await GetEmailSettings();
                if (emailSettings?.IsComplete() != true)
                {
                    Logs.Configuration.LogWarning("Should have sent email, but email settings are not configured");
                    return;
                }
                using (var smtp = emailSettings.CreateSmtpClient())
                {
                    var mail = emailSettings.CreateMailMessage(new MailAddress(email), subject, message);
                    mail.IsBodyHtml = true;
                    try
                    {
                        await smtp.SendMailAsync(mail).WithCancellation(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        smtp.SendAsyncCancel();
                    }
                }
           }, TimeSpan.Zero);
        }

        public abstract Task<EmailSettings> GetEmailSettings();
    }
}
