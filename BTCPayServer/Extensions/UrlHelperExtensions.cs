
using System;
using BTCPayServer;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc
{
    public static class UrlHelperExtensions
    {
#nullable enable
        public static string? EnsureLocal(this IUrlHelper helper, string? url, HttpRequest? httpRequest = null)
        {
            if (url is null || helper.IsLocalUrl(url))
                return url;
            if (httpRequest is null)
                return null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var r) && r.Host.Equals(httpRequest.Host.Host) && (!httpRequest.IsHttps || r.Scheme == "https"))
                return url;
            return null;
        }
#nullable restore
        public static string EmailConfirmationLink(this LinkGenerator urlHelper, string userId, string code, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(nameof(UIAccountController.ConfirmEmail), "UIAccount",
                new { userId, code }, scheme, host, pathbase);
        }

        public static string ResetPasswordCallbackLink(this LinkGenerator urlHelper, string userId, string code, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIAccountController.SetPassword),
                controller: "UIAccount",
                values: new { userId, code },
                scheme: scheme,
                host: host,
                pathBase: pathbase
            );
        }

        public static string PaymentRequestLink(this LinkGenerator urlHelper, string paymentRequestId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIPaymentRequestController.ViewPaymentRequest),
                controller: "UIPaymentRequest",
                values: new { payReqId = paymentRequestId },
                scheme, host, pathbase);
        }

        public static string AppLink(this LinkGenerator urlHelper, string appId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIAppsController.RedirectToApp),
                controller: "UIApps",
                values: new { appId },
                scheme, host, pathbase);
        }

        public static string InvoiceLink(this LinkGenerator urlHelper, string invoiceId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIInvoiceController.Invoice),
                controller: "UIInvoice",
                values: new { invoiceId },
                scheme, host, pathbase);
        }

        public static string CheckoutLink(this LinkGenerator urlHelper, string invoiceId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIInvoiceController.Checkout),
                controller: "UIInvoice",
                values: new { invoiceId },
                scheme, host, pathbase);
        }

        public static string PayoutLink(this LinkGenerator urlHelper, string walletIdOrStoreId, string pullPaymentId, PayoutState payoutState, string scheme, HostString host, string pathbase)
        {
            WalletId.TryParse(walletIdOrStoreId, out var wallet);
            return urlHelper.GetUriByAction(
                action: nameof(UIStorePullPaymentsController.Payouts),
                controller: "UIStorePullPayments",
                values: new { storeId = wallet?.StoreId ?? walletIdOrStoreId, pullPaymentId, payoutState },
                scheme, host, pathbase);
        }
    }
}
