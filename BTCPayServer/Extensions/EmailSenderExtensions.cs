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
            var button = $"{BUTTON_HTML}".Replace("{button_description}", actionName, System.StringComparison.InvariantCulture);
            return button.Replace("{button_link}", HtmlEncoder.Default.Encode(actionLink), System.StringComparison.InvariantCulture);
        }

        private static string CreateEmailBody(string body)
        {
            return $"<html><body style='{BODY_STYLE}'>{HEADER_HTML}{body}</body></html>";
        }

        public static void SendEmailConfirmation(this IEmailSender emailSender, MailboxAddress address, string link)
        {
            emailSender.SendEmail(address, "Confirm your email", CreateEmailBody(
                $"Please confirm your account.<br/><br/>{CallToAction("Confirm Email", link)}"));
        }

        public static void SendApprovalConfirmation(this IEmailSender emailSender, MailboxAddress address, string link)
        {
            emailSender.SendEmail(address, "Your account has been approved", CreateEmailBody(
                $"Your account has been approved and you can now <a href='{HtmlEncoder.Default.Encode(link)}'>login here</a>."));
        }

        public static void SendResetPassword(this IEmailSender emailSender, MailboxAddress address, string link)
        {
            emailSender.SendEmail(address, "Update Password", CreateEmailBody(
                $"A request has been made to reset your BTCPay Server password. Please set your password by clicking below.<br/><br/>{CallToAction("Update Password", link)}"));
        }

        public static void SendInvitation(this IEmailSender emailSender, MailboxAddress address, string link)
        {
            emailSender.SendEmail(address, "Invitation", CreateEmailBody(
                $"Please complete your account setup by clicking <a href='{HtmlEncoder.Default.Encode(link)}'>this link</a>."));
        }

        public static void SendNewUserInfo(this IEmailSender emailSender, MailboxAddress address, string newUserInfo, string link)
        {
            emailSender.SendEmail(address, newUserInfo, CreateEmailBody(
                $"{newUserInfo}. You can verify and approve the account here: <a href='{HtmlEncoder.Default.Encode(link)}'>User details</a>"));
        }

        public static void SendUserInviteAcceptedInfo(this IEmailSender emailSender, MailboxAddress address, string userInfo, string link)
        {
            emailSender.SendEmail(address, userInfo, CreateEmailBody(
                $"{userInfo}. You can view the store users here: <a href='{HtmlEncoder.Default.Encode(link)}'>Store users</a>"));
        }
    }
}
