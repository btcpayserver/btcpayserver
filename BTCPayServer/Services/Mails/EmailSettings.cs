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
        public string Server
        {
            get; set;
        }

        public int? Port
        {
            get; set;
        }

        public String Login
        {
            get; set;
        }

        public String Password
        {
            get; set;
        }
        [EmailAddress]
        public string From
        {
            get; set;
        }

        public bool EnableSSL
        {
            get; set;
        }

        public bool IsComplete()
        {
            SmtpClient smtp = null;
            try
            {
                smtp = CreateSmtpClient();
                return true;
            }
            catch { }
            return false;
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
