using System.Net.Mail;
using MimeKit;

namespace BTCPayServer.Client.Models
{
    public class SendEmailRequest
    {
        public string toName;
        public string toEmail;
        public string subject;
        public string body;

        public MailboxAddress toMailAddress()
        {
            return new MailboxAddress(toEmail, toName);
        }
    }
}
