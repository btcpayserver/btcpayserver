using System.Text.Encodings.Web;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Services
{
    public static class EmailSenderExtensions
    {
        private static string EMAIL_STYLE = @"<style>
        body {
            font-family: Open Sans, Helvetica Neue,Arial,sans-serif;
            font-color: #292929;
        }
        .btn-primary {
            color: #fff;
            background-color: #51b13e;
            border-color: #51b13e;
        }
        .btn {
            min-width: 2em;
            min-height: 20px;
            text-decoration-line: none;
            cursor: pointer;
            display: inline-block;
            font-weight: 400;
            color: #fff;
            text-align: center;
            vertical-align: middle;
            user-select: none;
            background-color: #51b13e;
            border: 1px solid transparent;
            padding: 0.375rem 0.75rem;
            font-size: 1rem;
            line-height: 1.5;
            border-radius: 0.25rem;
            transition: color 0.15s ease-in-out, background-color 0.15s ease-in-out, border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;
        }
        </style>";
        private static string BTCPAYSERVER_LOGO = @"<h2><img src='/img/btcpay-logo.svg' alt='BTCPay Server' style='height: 1.2em; min-width: 0.5em; margin-right: .8rem; vertical-align: top;'>BTCPay Server</h2>";
        //TODO Base64 the logo so that there is no dependency on the logo.
        private static string EMAIL_BUTTON = "<a href='{button_link}' type='submit' class='btn btn-primary'>{button_description}</a>";

        private static string CallToAction(string actionName, string actionLink)
        {
            string button = $"{EMAIL_BUTTON}".Replace("{button_description}", actionName, System.StringComparison.InvariantCulture);
            button = button.Replace("{button_link}", actionLink, System.StringComparison.InvariantCulture);
            return button;
        }

        public static void SendEmailConfirmation(this IEmailSender emailSender, string email, string link)
        {
            emailSender.SendEmail(email, "Confirm your email",
                $"Please confirm your account by clicking this link: <a href='{HtmlEncoder.Default.Encode(link)}'>link</a>");
        }
        
        public static void SendSetPasswordConfirmation(this IEmailSender emailSender, string email, string link, bool newPassword)
        {
            var subject = $"{(newPassword ? "Set" : "Update")}  Password";
            var body = $"A request has been made to update your BTCPay Server password. Please confirm your password by clicking below. <br/><br/> {CallToAction("Update Password", HtmlEncoder.Default.Encode(link))}";
            emailSender.SendEmail(email,
                subject,
                $"<html><head>{EMAIL_STYLE}</head>{BTCPAYSERVER_LOGO}<body>{body}</body></html>");
        }
    }
}
