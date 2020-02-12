using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Mails
{
    public class EmailSettings
    {
        [Display(Name = "SMTP Server")]
        public string Server
        {
            get; set;
        }

        public int? Port
        {
            get; set;
        }

        public string Login
        {
            get; set;
        }
        
        public string Password
        {
            get; set;
        }

        [Display(Name = "Sender's display name")]
        public string FromDisplay
        {
            get; set;
        }

        [EmailAddress]
        [Display(Name = "Sender's email address")]
        public string From
        {
            get; set;
        }

        [Display(Name = "Enable SSL")]
        public bool EnableSSL
        {
            get; set;
        }

        public bool IsComplete()
        {
            try
            {
                using var smtp = CreateSmtpClient();
                return true;
            }
            catch { }
            return false;
        }

        public MailMessage CreateMailMessage(MailAddress to, string subject, string message)
        {
            return new MailMessage(
                from: new MailAddress(From, FromDisplay),
                to: to)
            {
                Subject = subject,
                Body = message
            };
        }

        public SmtpClient CreateSmtpClient()
        {
            SmtpClient client = new SmtpClient(Server, Port.Value);
            client.EnableSsl = EnableSSL;
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(Login, Password);
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.Timeout = 10000;
            return client;
        }
    }
}
