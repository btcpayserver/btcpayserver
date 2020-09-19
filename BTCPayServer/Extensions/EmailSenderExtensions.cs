using System.Text.Encodings.Web;
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
        
        public static void SendSetPasswordConfirmation(this IEmailSender emailSender, string email, string link, bool newPassword)
        {
            emailSender.SendEmail(email,
                $"{(newPassword ? "Set" : "Reset")} Password",
                $"Please {(newPassword ? "set" : "reset")} your password by clicking here: <a href='{link}'>link</a>");
        }
    }
}
