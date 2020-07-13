using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security.GreenField
{
    public class BasicAuthenticationHandler : AuthenticationHandler<GreenFieldAuthenticationOptions>
    {
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public BasicAuthenticationHandler(
            IOptionsMonitor<IdentityOptions> identityOptions,
            IOptionsMonitor<GreenFieldAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) : base(options, logger, encoder, clock)
        {
            _identityOptions = identityOptions;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string authHeader = Context.Request.Headers["Authorization"];

            if (authHeader == null || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.NoResult();
            string password;
            string username;
            try
            {
                var encodedUsernamePassword =
                    authHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[1]?.Trim();
                var decodedUsernamePassword =
                    Encoding.UTF8.GetString(Convert.FromBase64String(encodedUsernamePassword)).Split(':');
                username = decodedUsernamePassword[0];
                password = decodedUsernamePassword[1];
            }
            catch (Exception)
            {
                return AuthenticateResult.Fail(
                    "Basic authentication header was not in a correct format. (username:password encoded in base64)");
            }

            var result = await _signInManager.PasswordSignInAsync(username, password, true, true);
            if (!result.Succeeded)
                return AuthenticateResult.Fail(result.ToString());

            var user = await _userManager.FindByNameAsync(username);
            var claims = new List<Claim>()
            {
                new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, user.Id),
                new Claim(GreenFieldConstants.ClaimTypes.Permission,
                    Permission.Create(Policies.Unrestricted).ToString())
            };

            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, GreenFieldConstants.AuthenticationType)),
                GreenFieldConstants.AuthenticationType));
        }
    }
}
