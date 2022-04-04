using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using MailKit.Net.Smtp;
using MimeKit;

namespace BTCPayServer.Services.Mails
{
    public class EmailSettings : EmailSettingsData
    {
        public bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(Server) && Port is int;
        }

        public MimeMessage CreateMailMessage(MailboxAddress to, string subject, string message, bool isHtml) =>
            CreateMailMessage(new[] {to}, null, null, subject, message, isHtml);
        public MimeMessage CreateMailMessage(MailboxAddress[] to, MailboxAddress[] cc, MailboxAddress[] bcc, string subject, string message, bool isHtml)
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

            var mm = new MimeMessage();
            mm.Body = bodyBuilder.ToMessageBody();
            mm.Subject = subject;
            mm.From.Add(new MailboxAddress(From, !string.IsNullOrWhiteSpace(FromDisplay) ? From : FromDisplay));
            mm.To.AddRange(to);
            mm.Cc.AddRange(cc?? System.Array.Empty<InternetAddress>());
            mm.Bcc.AddRange(bcc?? System.Array.Empty<InternetAddress>());
            return mm;
            
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
                if ((client.Capabilities & SmtpCapabilities.Authentication) != 0)
                    await client.AuthenticateAsync(Login ?? string.Empty, Password ?? string.Empty, connectCancel.Token);
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
