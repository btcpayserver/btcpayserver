
using BTCPayServer;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Apps;
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

        public static string ResetPasswordCallbackLink(this LinkGenerator urlHelper, string userId, string code, string scheme,  HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(AccountController.SetPassword),
                controller: "Account",
                values: new { userId, code },
                scheme: scheme,
                host:host,
                pathBase: pathbase
            );
        }

        public static string PaymentRequestLink(this LinkGenerator urlHelper, string paymentRequestId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(PaymentRequestController.ViewPaymentRequest),
                controller: "PaymentRequest",
                values: new { id = paymentRequestId },
                scheme, host, pathbase);
        }

        public static string AppLink(this LinkGenerator urlHelper, string appId,  string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(AppsPublicController.RedirectToApp),
                controller: "AppsPublic",
                values: new {  appId },
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

        public static string CheckoutLink(this LinkGenerator urlHelper, string invoiceId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(InvoiceController.Checkout),
                controller: "Invoice",
                values: new { invoiceId = invoiceId },
                scheme, host, pathbase);
        }

        public static string PayoutLink(this LinkGenerator urlHelper, string walletIdOrStoreId,string pullPaymentId, string scheme, HostString host, string pathbase)
        {
            WalletId.TryParse(walletIdOrStoreId, out var wallet);
            return urlHelper.GetUriByAction(
                action: nameof(StorePullPaymentsController.Payouts),
                controller: "StorePullPayments",
                values: new {storeId= wallet?.StoreId?? walletIdOrStoreId , pullPaymentId},
                scheme, host, pathbase);
        }
    }
}
