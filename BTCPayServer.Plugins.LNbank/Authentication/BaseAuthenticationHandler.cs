using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.LNbank.Authentication
{
    public abstract class BaseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        protected readonly UserManager<ApplicationUser> UserManager;

        protected BaseAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            UserManager<ApplicationUser> userManager) : base(options, logger, encoder, clock)
        {
            UserManager = userManager;
        }

        protected async Task<AuthenticateResult> AuthenticateUser(ApplicationUser user, string scheme)
        {
            
            var isAdmin = await UserManager.IsInRoleAsync(user, "ServerAdmin");
            var claims = new List<Claim>
            {
                new Claim("UserId", user.Id),
                new Claim("IsAdmin", isAdmin.ToString())
            };
            var claimsIdentity = new ClaimsIdentity(claims, scheme);
            var principal = new ClaimsPrincipal(claimsIdentity);
            var ticket = new AuthenticationTicket(principal, scheme);

            return AuthenticateResult.Success(ticket);
        }
    }
}
