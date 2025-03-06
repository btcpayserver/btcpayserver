using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Validation;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MimeKit;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Mails
{
    /// <summary>
    /// Email Settings gets saved to database, and is used by both the server and store settings
    /// </summary>
    public class EmailSettings
    {
        public string Server { get; set; }
        public int? Port { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string From { get; set; }
        public bool DisableCertificateCheck { get; set; }
        [JsonIgnore]
        public bool EnabledCertificateCheck
        {
            get => !DisableCertificateCheck;
            set { DisableCertificateCheck = !value; }
        }
        
        // Conversion functions for mapping to and from the client EmailSettingsData model
#nullable enable
        public static EmailSettings FromData(EmailSettingsData data, string? existingPassword)
            => new()
            {
                Server = data.Server,
                Port = data.Port,
                Login = data.Login,
                // If login was provided but password was not, use the existing password from database
                Password = !string.IsNullOrEmpty(data.Login) && string.IsNullOrEmpty(data.Password) ? 
                    existingPassword : data.Password,
                From = data.From,
                DisableCertificateCheck = data.DisableCertificateCheck
            };
        
        public T ToData<T>() where T : EmailSettingsData, new()
            => new T()
            {
                Server = Server,
                Port = Port,
                Login = Login,
                PasswordSet = !string.IsNullOrEmpty(Password),
                From = From,
                DisableCertificateCheck = DisableCertificateCheck
            };
#nullable restore
        
        
        // 
        public bool IsComplete()
        {
            return MailboxAddressValidator.IsMailboxAddress(From)
                && !string.IsNullOrWhiteSpace(Server)
                && Port != null;
        }

        public void Validate(string prefixKey, ModelStateDictionary modelState)
        {
            if (string.IsNullOrWhiteSpace(From))
            {
                modelState.AddModelError($"{prefixKey}{nameof(From)}", new RequiredAttribute().FormatErrorMessage(nameof(From)));
            }
            if (!MailboxAddressValidator.IsMailboxAddress(From))
            {
                modelState.AddModelError($"{prefixKey}{nameof(From)}", MailboxAddressAttribute.ErrorMessageConst);
            }
            if (string.IsNullOrWhiteSpace(Server))
            {
                modelState.AddModelError($"{prefixKey}{nameof(Server)}", new RequiredAttribute().FormatErrorMessage(nameof(Server)));
            }
            if (Port is null)
            {
                modelState.AddModelError($"{prefixKey}{nameof(Port)}", new RequiredAttribute().FormatErrorMessage(nameof(Port)));
            }
        }

        public MimeMessage CreateMailMessage(MailboxAddress to, string subject, string message, bool isHtml) =>
            CreateMailMessage(new[] { to }, null, null, subject, message, isHtml);
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
            mm.From.Add(MailboxAddressValidator.Parse(From));
            mm.To.AddRange(to);
            mm.Cc.AddRange(cc ?? System.Array.Empty<InternetAddress>());
            mm.Bcc.AddRange(bcc ?? System.Array.Empty<InternetAddress>());
            return mm;
        }

        public async Task<SmtpClient> CreateSmtpClient()
        {
            SmtpClient client = new SmtpClient();
            using var connectCancel = new CancellationTokenSource(10000);
            try
            {
                if (DisableCertificateCheck)
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
