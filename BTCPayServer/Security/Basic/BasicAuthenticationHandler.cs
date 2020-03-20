using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security.APIKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security.Basic
{
    public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
    {
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public BasicAuthenticationHandler(
            
            IOptionsMonitor<IdentityOptions> identityOptions,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IOptionsMonitor<BasicAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
            _identityOptions = identityOptions;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
        
            string authHeader = Context.Request.Headers["Authorization"];

            if (authHeader == null || !authHeader.StartsWith("Basic ")) return AuthenticateResult.NoResult();
            var encodedUsernamePassword = authHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[1]?.Trim();
            var decodedUsernamePassword = Encoding.UTF8.GetString(Convert.FromBase64String(encodedUsernamePassword)).Split(':');
            var username = decodedUsernamePassword[0];
            var password = decodedUsernamePassword[1];

            var result = await _signInManager.PasswordSignInAsync(username, password, true, true);
            if (!result.Succeeded) return AuthenticateResult.Fail(result.ToString());
                
            var user = await _userManager.FindByNameAsync(username);
            var claims = new List<Claim>()
            {
                new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, user.Id),
                new Claim(APIKeyConstants.ClaimTypes.Permission, Permission.Create(Policies.Unrestricted).ToString())
            };
            
            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, APIKeyConstants.AuthenticationType)), APIKeyConstants.AuthenticationType));

        }
    }
}
