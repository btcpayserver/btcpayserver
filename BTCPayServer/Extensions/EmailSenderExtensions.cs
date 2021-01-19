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
                $"Update Password",
                $"A request has been made for you to update the password for your BTCPay Server account. Please click <a href='{HtmlEncoder.Default.Encode(link)}'>here</a> to update your password. <br/><br/> Alternatively copy and paste this URL into your browser: <br/><br/> {HtmlEncoder.Default.Encode(link)}");            
        }
    }
}
