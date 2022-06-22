using MimeKit;

namespace BTCPayServer.Services.Mails
{
    public interface IEmailSender
    {
        void SendEmail(MailboxAddress email, string subject, string message);
        void SendEmail(MailboxAddress[] email, MailboxAddress[] cc, MailboxAddress[] bcc, string subject, string message);
    }
}
