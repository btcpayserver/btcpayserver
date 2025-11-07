using System.Text.Encodings.Web;
using BTCPayServer.Services.Mails;
using MimeKit;
using QRCoder;

namespace BTCPayServer.Services
{
    public static class EmailSenderExtensions
    {
        private static string BODY_STYLE = "font-family: Open Sans, Helvetica Neue,Arial,sans-serif; font-color: #292929;";
        private static string HEADER_HTML = "<h1 style='font-size:1.2rem'>BTCPay Server</h1><br/>";

        private static string CreateEmailBody(string body)
        {
            return $"<html><body style='{BODY_STYLE}'>{HEADER_HTML}{body}</body></html>";
        }

        public static void SendUserInviteAcceptedInfo(this IEmailSender emailSender, MailboxAddress address, string userInfo, string link)
        {
            emailSender.SendEmail(address, userInfo, CreateEmailBody(
                $"{userInfo}. You can view the store users here: <a href='{HtmlEncoder.Default.Encode(link)}'>Store users</a>"));
        }
    }
}
