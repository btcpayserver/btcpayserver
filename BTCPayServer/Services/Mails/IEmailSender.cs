namespace BTCPayServer.Services.Mails
{
    public interface IEmailSender
    {
        void SendEmail(string email, string subject, string message);
    }
}
