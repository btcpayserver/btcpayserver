using System;
using BTCPayServer;
using BTCPayServer.Abstractions;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc
{
    public static class UrlHelperExtensions
    {
#nullable enable
        public static string? WalletSend(this IUrlHelper helper, WalletId walletId) => helper.Action(nameof(UIWalletsController.WalletSend), new { walletId });
        public static string? WalletTransactions(this IUrlHelper helper, string walletId) => WalletTransactions(helper, WalletId.Parse(walletId));
        public static string? WalletTransactions(this IUrlHelper helper, WalletId walletId)
        => helper.Action(nameof(UIWalletsController.WalletTransactions), new { walletId });
        public static Uri ActionAbsolute(this IUrlHelper helper, HttpRequest request, string? action, string? controller, object? values)
        => request.GetAbsoluteUriNoPathBase(new Uri(helper.Action(action, controller, values) ?? "", UriKind.Relative));
        public static Uri ActionAbsolute(this IUrlHelper helper, HttpRequest request, string? action, string? controller)
=> request.GetAbsoluteUriNoPathBase(new Uri(helper.Action(action, controller) ?? "", UriKind.Relative));
        public static Uri ActionAbsolute(this IUrlHelper helper, HttpRequest request, string? action, object? values)
=> request.GetAbsoluteUriNoPathBase(new Uri(helper.Action(action, values) ?? "", UriKind.Relative));
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

        public static string LoginCodeLink(this LinkGenerator urlHelper, string loginCode, string returnUrl, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(nameof(UIAccountController.LoginUsingCode), "UIAccount", new { loginCode, returnUrl }, scheme, host, pathbase);
        }

        public static string PaymentRequestLink(this LinkGenerator urlHelper, string paymentRequestId, RequestBaseUrl baseUrl)
        => PaymentRequestLink(urlHelper, paymentRequestId, baseUrl.Scheme, baseUrl.Host, baseUrl.PathBase);
        public static string PaymentRequestLink(this LinkGenerator urlHelper, string paymentRequestId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIPaymentRequestController.ViewPaymentRequest),
                controller: "UIPaymentRequest",
                values: new { payReqId = paymentRequestId },
                scheme, host, pathbase);
        }

        public static string WalletTransactionsLink(this LinkGenerator urlHelper, WalletId walletId, RequestBaseUrl baseUrl)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIWalletsController.WalletTransactions),
                controller: "UIWallets",
                values: new { walletId = walletId.ToString() },
                baseUrl
            );
        }

        public static string AppLink(this LinkGenerator urlHelper, string appId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIAppsController.RedirectToApp),
                controller: "UIApps",
                values: new { appId },
                scheme, host, pathbase);
        }

        public static string InvoiceLink(this LinkGenerator urlHelper, string invoiceId, RequestBaseUrl baseUrl)
        => InvoiceLink(urlHelper, invoiceId, baseUrl.Scheme, baseUrl.Host, baseUrl.PathBase);
        public static string InvoiceLink(this LinkGenerator urlHelper, string invoiceId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIInvoiceController.Invoice),
                controller: "UIInvoice",
                values: new { invoiceId },
                scheme, host, pathbase);
        }

        public static string PullPaymentLink(this LinkGenerator urlHelper, string pullPaymentId, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIPullPaymentController.ViewPullPayment),
                controller: "UIPullPayment",
                values: new { pullPaymentId },
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
#nullable enable
        public static string ReceiptLink(this LinkGenerator urlHelper, string invoiceId, RequestBaseUrl baseUrl)
            => urlHelper.GetUriByAction(
                action: nameof(UIInvoiceController.InvoiceReceipt),
                controller: "UIInvoice",
                values: new { invoiceId },
                baseUrl);


        public static string InvoiceCheckoutLink(this LinkGenerator urlHelper, string invoiceId, RequestBaseUrl baseUrl)
            => urlHelper.GetUriByAction(
                    action: nameof(UIInvoiceController.Checkout),
                    controller: "UIInvoice",
                    values: new { invoiceId },
                    baseUrl
                );

        public static string GetUriByAction(
            this LinkGenerator generator,
            string action,
            string controller,
            object? values,
            RequestBaseUrl requestBaseUrl,
            FragmentString fragment = default,
            LinkOptions? options = null) => generator.GetUriByAction(action, controller, values, requestBaseUrl.Scheme, requestBaseUrl.Host, requestBaseUrl.PathBase, fragment, options) ?? throw new InvalidOperationException($"Bug, unable to generate link for {controller}.{action}");
#nullable restore
        public static string PayoutLink(this LinkGenerator urlHelper, string walletIdOrStoreId, string pullPaymentId, PayoutState payoutState, string scheme, HostString host, string pathbase)
        {
            WalletId.TryParse(walletIdOrStoreId, out var wallet);
            return urlHelper.GetUriByAction(
                action: nameof(UIStorePullPaymentsController.Payouts),
                controller: "UIStorePullPayments",
                values: new { storeId = wallet?.StoreId ?? walletIdOrStoreId, pullPaymentId, payoutState },
                scheme, host, pathbase);
        }

        public static string IndexLink(this LinkGenerator urlHelper, string scheme, HostString host, string pathbase)
        {
            return urlHelper.GetUriByAction(
                action: nameof(UIHomeController.Index),
                controller: "UIHome",
                values: null,
                scheme, host, pathbase);
        }
    }
}
