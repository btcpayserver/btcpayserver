using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Services
{
    public static class EmailSenderExtensions
    {
        public static void SendEmailConfirmation(this IEmailSender emailSender, string email, string link)
        {
            emailSender.SendEmail(email, "Confirm your email",
                $"Please confirm your account by clicking this link: <a href='{HtmlEncoder.Default.Encode(link)}'>link</a>");
        }
    }
}
