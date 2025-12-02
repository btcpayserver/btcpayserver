#nullable enable
using System;
using System.Reflection.Emit;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using NBitcoin.Altcoins.ArgoneumInternals;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http.Extensions;
using NBitcoin.DataEncoders;
using System.Runtime.CompilerServices;
using BTCPayServer.Abstractions;

namespace BTCPayServer.Services
{
    public class CallbackGenerator(
        LinkGenerator linkGenerator,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        public LinkGenerator LinkGenerator { get; } = linkGenerator;
        public UserManager<ApplicationUser> UserManager { get; } = userManager;
        public RequestBaseUrl? BaseUrl { get; set; }

        public string ForLNUrlAuth(ApplicationUser user, byte[] r)
        => LinkGenerator.GetUriByAction(
            action: nameof(UILNURLAuthController.LoginResponse),
            controller: "UILNURLAuth",
            values: new { userId = user.Id, action = "login", tag = "login", k1 = Encoders.Hex.EncodeData(r) },
            GetRequestBaseUrl());

        public RequestBaseUrl GetRequestBaseUrl()
        => BaseUrl ?? httpContextAccessor.HttpContext?.Request.GetRequestBaseUrl() ?? throw new InvalidOperationException($"You should be in a HttpContext to call this method");

        public string StoreUsersLink(string storeId)
        => LinkGenerator.GetUriByAction(nameof(UIStoresController.StoreUsers), "UIStores",
            new { storeId }, GetRequestBaseUrl());

        public async Task<string> ForEmailConfirmation(ApplicationUser user)
        {
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            return LinkGenerator.GetUriByAction(nameof(UIAccountController.ConfirmEmail), "UIAccount",
                new { userId = user.Id, code }, GetRequestBaseUrl());
        }

        public async Task<string> ForInvitation(ApplicationUser user)
        {
            var code = await UserManager.GenerateInvitationTokenAsync(user.Id);
            return ForInvitation(user.Id, code ?? "???");
        }

        public string ForInvitation(string userId, string code)
        => LinkGenerator.GetUriByAction(nameof(UIAccountController.AcceptInvite), "UIAccount",
            new { userId, code }, GetRequestBaseUrl());

        public async Task<string> ForPasswordReset(ApplicationUser user)
        {
            var code = await UserManager.GeneratePasswordResetTokenAsync(user);
            return LinkGenerator.GetUriByAction(
                action: nameof(UIAccountController.SetPassword),
                controller: "UIAccount",
                values: new { userId = user.Id, code },
                GetRequestBaseUrl());
        }

        public string ForApproval(ApplicationUser user)
        => LinkGenerator.GetUriByAction(nameof(UIServerController.User), "UIServer",
            new { userId = user.Id }, GetRequestBaseUrl());

        public string ForLogin(ApplicationUser user)
        => LinkGenerator.GetUriByAction(nameof(UIAccountController.Login), "UIAccount",
            new { email = user.Email }, GetRequestBaseUrl());

        public string WalletTransactionsLink(WalletId walletId)
        => LinkGenerator.GetUriByAction(
            action: nameof(UIWalletsController.WalletTransactions),
            controller: "UIWallets",
            values: new { walletId = walletId.ToString() },
            GetRequestBaseUrl());

        public string PaymentRequestByIdLink(string payReqId)
        => LinkGenerator.GetUriByAction(
            action: nameof(UIPaymentRequestController.ViewPaymentRequest),
            controller: "UIPaymentRequest",
            values: new { payReqId },
            GetRequestBaseUrl());

        public string PaymentRequestListLink(string storeId)
        => LinkGenerator.GetUriByAction(
            action: nameof(UIPaymentRequestController.GetPaymentRequests),
            controller: "UIPaymentRequest",
            values: new { storeId },
            GetRequestBaseUrl());

        public string InvoiceLink(string invoiceId)
        => LinkGenerator.GetUriByAction(
            action: nameof(UIInvoiceController.Invoice),
            controller: "UIInvoice",
            values: new { invoiceId },
            GetRequestBaseUrl());
    }
}
