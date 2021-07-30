using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.LNbank.Authentication
{
    public class BasicAuthenticationHandler : BaseAuthenticationHandler
    {
        public BasicAuthenticationHandler(
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
            if (authHeader == null || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.NoResult();

            string userId, apiKey;
            try
            {
                var encoded = authHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[1].Trim();
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split(':');
                userId = decoded[0];
                apiKey = decoded[1];
            }
            catch (Exception)
            {
                return AuthenticateResult.Fail("Basic authentication header was not in a correct format. (userId:apiKey base64 encoded)");
            }

            try
            {
                ApplicationUser user = await UserManager.GetUserAsync(userId);
                return await AuthenticateUser(user, AuthenticationSchemes.ApiBasic);
            }
            catch (Exception exception)
            {
                return AuthenticateResult.Fail($"Authentication failed! {exception}");
            }
        }
    }
}
