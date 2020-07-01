
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc
{
    public static class UrlHelperExtensions
    {
        public static string EmailConfirmationLink(this LinkGenerator urlHelper, string userId, string code, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(nameof(AccountController.ConfirmEmail), "Account",
                new { userId, code }, scheme, host, pathbase);
        }

        public static string ResetPasswordCallbackLink(this IUrlHelper urlHelper, string userId, string code, string scheme)
        {
            return urlHelper.Action(
                action: nameof(AccountController.ResetPassword),
                controller: "Account",
                values: new { userId, code },
                protocol: scheme);
        }

        public static string PaymentRequestLink(this LinkGenerator urlHelper, string paymentRequestId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(PaymentRequestController.ViewPaymentRequest),
                controller: "PaymentRequest",
                values: new { id = paymentRequestId },
                scheme, host, pathbase);
        }

        public static string InvoiceLink(this LinkGenerator urlHelper, string invoiceId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(InvoiceController.Invoice),
                controller: "Invoice",
                values: new { invoiceId = invoiceId },
                scheme, host, pathbase);
        }
    }
}
