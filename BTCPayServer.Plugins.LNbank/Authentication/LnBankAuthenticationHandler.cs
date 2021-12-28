using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.LNbank.Authentication
{
    
    public class LnBankAuthenticationOptions : AuthenticationSchemeOptions
    {
    }
    public class LnBankAuthenticationHandler : AuthenticationHandler<LnBankAuthenticationOptions>
    {
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly WalletService _walletService;

        public LnBankAuthenticationHandler(
            IOptionsMonitor<IdentityOptions> identityOptions,
            IOptionsMonitor<LnBankAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            WalletService walletService) : base(options, logger, encoder, clock)
        {
            _identityOptions = identityOptions;
            _walletService = walletService;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string authHeader = Context.Request.Headers["Authorization"];
            if (authHeader == null || !authHeader.StartsWith("Bearer ", StringComparison.InvariantCultureIgnoreCase))
                return AuthenticateResult.NoResult();

            string apiKey = authHeader.Substring("Bearer ".Length);
            var wallet =
                await _walletService.GetWallet(new WalletQuery() { IncludeTransactions = false, AccessKey = apiKey });
            if (wallet is null)
            {
                return AuthenticateResult.Fail("Incorrect wallet key");
            }

            var claims = new List<Claim>
            {
                new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, wallet.UserId),
                new Claim("WalletId", wallet.WalletId)
            };
            var claimsIdentity = new ClaimsIdentity(claims, AuthenticationSchemes.Api);
            var principal = new ClaimsPrincipal(claimsIdentity);
            var ticket = new AuthenticationTicket(principal, AuthenticationSchemes.Api);
            Context.Items.Add("Wallet", wallet);
            return AuthenticateResult.Success(ticket);
        }
    }
}
