using System.Net.Mail;

namespace BTCPayServer.Client.Models
{
    public class SendEmailRequest
    {
        public string toName;
        public string toEmail;
        public string subject;
        public string body;

        public MailAddress toMailAddress()
        {
            return new MailAddress(toEmail, toName);
        }
    }
}
