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

namespace BTCPayServer.Services
{
    public class CallbackGenerator(LinkGenerator linkGenerator, UserManager<ApplicationUser> userManager)
    {
        public LinkGenerator LinkGenerator { get; } = linkGenerator;
        public UserManager<ApplicationUser> UserManager { get; } = userManager;

        public string ForLNUrlAuth(ApplicationUser user, byte[] r, HttpRequest request)
        {
            return LinkGenerator.GetUriByAction(
                        action: nameof(UILNURLAuthController.LoginResponse),
                        controller: "UILNURLAuth",
                        values: new { userId = user.Id, action = "login", tag = "login", k1 = Encoders.Hex.EncodeData(r) },
                        request.Scheme,
                        request.Host,
                        request.PathBase) ?? throw Bug();
        }

        public string StoreUsersLink(string storeId, HttpRequest request)
        {
            return LinkGenerator.GetUriByAction(nameof(UIStoresController.StoreUsers), "UIStores",
                new { storeId }, request.Scheme, request.Host, request.PathBase) ?? throw Bug();
        }

        public async Task<string> ForEmailConfirmation(ApplicationUser user, HttpRequest request)
        {
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            return LinkGenerator.GetUriByAction(nameof(UIAccountController.ConfirmEmail), "UIAccount",
                new { userId = user.Id, code }, request.Scheme, request.Host, request.PathBase) ?? throw Bug();
        }
        public async Task<string> ForInvitation(ApplicationUser user, HttpRequest request)
        {
            var code = await UserManager.GenerateInvitationTokenAsync<ApplicationUser>(user.Id) ?? throw Bug();
            return ForInvitation(user, code, request);
        }
        public string ForInvitation(ApplicationUser user, string code, HttpRequest request)
        {
            return LinkGenerator.GetUriByAction(nameof(UIAccountController.AcceptInvite), "UIAccount",
                new { userId = user.Id, code }, request.Scheme, request.Host, request.PathBase) ?? throw Bug();
        }
        public async Task<string> ForPasswordReset(ApplicationUser user, HttpRequest request)
        {
            var code = await UserManager.GeneratePasswordResetTokenAsync(user);
            return LinkGenerator.GetUriByAction(
                action: nameof(UIAccountController.SetPassword),
                controller: "UIAccount",
                values: new { userId = user.Id, code },
                scheme: request.Scheme,
                host: request.Host,
                pathBase: request.PathBase
            ) ?? throw Bug();
        }

        public string ForApproval(ApplicationUser user, HttpRequest request)
        {
            return LinkGenerator.GetUriByAction(nameof(UIServerController.User), "UIServer",
                new { userId = user.Id }, request.Scheme, request.Host, request.PathBase) ?? throw Bug();
        }
        public string ForLogin(ApplicationUser user, HttpRequest request)
        {
            return LinkGenerator.GetUriByAction(nameof(UIAccountController.Login), "UIAccount", new { email = user.Email }, request.Scheme, request.Host, request.PathBase) ?? throw Bug();
        }

        private Exception Bug([CallerMemberName] string? name = null) => new InvalidOperationException($"Error generating link for {name} (Report this bug to BTCPay Server github repository)");

        public string WalletTransactionsLink(WalletId walletId, HttpRequest request)
        {
            return LinkGenerator.GetUriByAction(
                action: nameof(UIWalletsController.WalletTransactions),
                controller: "UIWallets",
                values: new { walletId = walletId.ToString() },
                scheme: request.Scheme,
                host: request.Host,
                pathBase: request.PathBase
            ) ?? throw Bug();
        }
    }
}
