namespace BTCPayServer.Services.Mails
{
    public interface IEmailSender
    {
        void SendEmail(string email, string subject, string message);
        void SendEmail(string[] email, string[] cc, string[] bcc, string subject, string message);
    }
}
