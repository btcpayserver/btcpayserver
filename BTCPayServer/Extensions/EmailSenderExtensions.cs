using System.Text.Encodings.Web;
using BTCPayServer.Services.Mails;
using MimeKit;

namespace BTCPayServer.Services
{
    public static class EmailSenderExtensions
    {
        private static string BODY_STYLE = "font-family: Open Sans, Helvetica Neue,Arial,sans-serif; font-color: #292929;";
        private static string HEADER_HTML = "<h1 style='font-size:1.2rem'>BTCPay Server</h1><br/>";
        private static string BUTTON_HTML = "<a href='{button_link}' type='submit' style='min-width: 2em;min-height: 20px;text-decoration-line: none;cursor: pointer;display: inline-block;font-weight: 400;color: #fff;text-align: center;vertical-align: middle;user-select: none;background-color: #51b13e;border-color: #51b13e;border: 1px solid transparent;padding: 0.375rem 0.75rem;font-size: 1rem;line-height: 1.5;border-radius: 0.25rem;transition: color 0.15s ease-in-out, background-color 0.15s ease-in-out, border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;'>{button_description}</a>";

        private static string CallToAction(string actionName, string actionLink)
        {
            string button = $"{BUTTON_HTML}".Replace("{button_description}", actionName, System.StringComparison.InvariantCulture);
            button = button.Replace("{button_link}", actionLink, System.StringComparison.InvariantCulture);
            return button;
        }

        public static void SendEmailConfirmation(this IEmailSender emailSender, MailboxAddress address, string link)
        {
            emailSender.SendEmail(address, "Confirm your email",
                $"Please confirm your account by clicking this link: <a href='{HtmlEncoder.Default.Encode(link)}'>link</a>");
        }

        public static void SendSetPasswordConfirmation(this IEmailSender emailSender, MailboxAddress address, string link, bool newPassword)
        {
            var subject = $"{(newPassword ? "Set" : "Update")}  Password";
            var body = $"A request has been made to {(newPassword ? "set" : "update")} your BTCPay Server password. Please confirm your password by clicking below. <br/><br/> {CallToAction(subject, HtmlEncoder.Default.Encode(link))}";
            emailSender.SendEmail(address, subject, $"<html><body style='{BODY_STYLE}'>{HEADER_HTML}{body}</body></html>");
        }
    }
}
