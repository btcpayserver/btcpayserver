using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Validation;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MimeKit;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Emails.Services;

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
    public string? GetPasswordFor(string? server)
        => string.Equals(Server, server, System.StringComparison.OrdinalIgnoreCase) ? Password : null;

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
               && Port != null
               && !string.IsNullOrWhiteSpace(From)
               && MailboxAddressValidator.IsMailboxAddress(From);
    }

    public void Validate(string prefixKey, ModelStateDictionary modelState)
    {
        if (!string.IsNullOrEmpty(From) && !MailboxAddressValidator.IsMailboxAddress(From))
        {
            modelState.AddModelError($"{prefixKey}{nameof(From)}", MailboxAddressAttribute.ErrorMessageConst);
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

    [Obsolete("Use CreateSmtpClient(BTCPayServerOptions) to apply SSRF protection, or EmailSender.CreateSmtpClient for store email.")]
    public Task<SmtpClient> CreateSmtpClient() => CreateSmtpClient(null);

    public async Task<SmtpClient> CreateSmtpClient(BTCPayServerOptions ssrfProtection)
    {
        // MailKit uses port 25 for port 0 with SecureSocketOptions.Auto.
        var port = Port.Value == 0 ? 25 : Port.Value;
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
            if (ssrfProtection is { DisableSSRFProtection: false })
            {
                var socket = await SSRFProtectionExtensions.ConnectSocket(Server, port, ssrfProtection.SSRFExceptions, connectCancel.Token);
                try
                {
                    await client.ConnectAsync(socket, Server, port, MailKit.Security.SecureSocketOptions.Auto, connectCancel.Token);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            else
            {
                await client.ConnectAsync(Server, port, MailKit.Security.SecureSocketOptions.Auto, connectCancel.Token);
            }
            if (client.Capabilities.Contains(SmtpCapability.Authentication)
                && !string.IsNullOrWhiteSpace(Login)
                && !string.IsNullOrWhiteSpace(Password))
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
