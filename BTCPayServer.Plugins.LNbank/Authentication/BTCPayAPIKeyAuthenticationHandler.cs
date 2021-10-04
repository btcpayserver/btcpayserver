using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.LNbank.Authentication
{
    public class BTCPayAPIKeyAuthenticationHandler : BaseAuthenticationHandler
    {
        public BTCPayAPIKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options, 
            ILoggerFactory logger, 
            UrlEncoder encoder, 
            ISystemClock clock,
            UserManager<ApplicationUser> userManager) : base(options, logger, encoder, clock, userManager)
        {
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string authHeader = Context.Request.Headers["Authorization"];
            if (authHeader == null || !authHeader.StartsWith("Bearer ", StringComparison.InvariantCultureIgnoreCase))
                return AuthenticateResult.NoResult();

            string apiKey = authHeader.Substring("Bearer ".Length);

            try
            {
                // TODO: Needs proper implementation
                return AuthenticateResult.Fail("Authentication failed! Not implemented.");
                /*
                ApplicationUser user = await UserManager.FindUserByBtcPayApiKey(apiKey);
                return await AuthenticateUser(user, AuthenticationSchemes.ApiBTCPayAPIKey);
                */
            }
            catch (Exception exception)
            {
                return AuthenticateResult.Fail($"Authentication failed! {exception}");
            }
        }
    }
}
