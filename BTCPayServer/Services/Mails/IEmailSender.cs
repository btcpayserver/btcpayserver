using System.Net.Mail;

namespace BTCPayServer.Services.Mails
{
    public interface IEmailSender
    {
        void SendEmail(MailAddress email, string subject, string message);
        void SendEmail(MailAddress[] email, MailAddress[] cc, MailAddress[] bcc, string subject, string message);
    }
}
