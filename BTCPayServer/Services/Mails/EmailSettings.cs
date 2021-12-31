using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MimeKit;
using Newtonsoft.Json;

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

        public bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(Server) &&
                   Port is int &&
                   !string.IsNullOrWhiteSpace(Login) &&
                   !string.IsNullOrWhiteSpace(Password);
        }

        public MimeMessage CreateMailMessage(MailboxAddress to, string subject, string message, bool isHtml)
        {
            var bodyBuilder = new BodyBuilder();
            if (isHtml)
            {
                bodyBuilder.HtmlBody = message;
            }
            else
            {
                bodyBuilder.TextBody = message;
            }

            return new MimeMessage(
                from: new[] { new MailboxAddress(From, !string.IsNullOrWhiteSpace(FromDisplay) ? From : FromDisplay) },
                to: new[] { to },
                subject,
                bodyBuilder.ToMessageBody());
        }

        public async Task<SmtpClient> CreateSmtpClient()
        {
            SmtpClient client = new SmtpClient();
            using var connectCancel = new CancellationTokenSource(10000);
            try
            {
                if (Extensions.IsLocalNetwork(Server))
                {
                    client.CheckCertificateRevocation = false;
#pragma warning disable CA5359 // Do Not Disable Certificate Validation
                    client.ServerCertificateValidationCallback = (s, c, h, e) => true;
#pragma warning restore CA5359 // Do Not Disable Certificate Validation
                }
                await client.ConnectAsync(Server, Port.Value, MailKit.Security.SecureSocketOptions.Auto, connectCancel.Token);
                await client.AuthenticateAsync(Login, Password, connectCancel.Token);
            }
            catch
            {
                client.Dispose();
                throw;
            }
            return client;
        }
    }
}
