using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
    public class APIKeysAuthenticationHandler : AuthenticationHandler<GreenFieldAuthenticationOptions>
    {
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public APIKeysAuthenticationHandler(
            APIKeyRepository apiKeyRepository,
            IOptionsMonitor<IdentityOptions> identityOptions,
            IOptionsMonitor<GreenFieldAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) : base(options, logger, encoder, clock)
        {
            _apiKeyRepository = apiKeyRepository;
            _identityOptions = identityOptions;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Context.Request.HttpContext.GetAPIKey(out var apiKey) || string.IsNullOrEmpty(apiKey))
                return AuthenticateResult.NoResult();

            var key = await _apiKeyRepository.GetKey(apiKey);

            if (key == null)
            {
                return AuthenticateResult.Fail("ApiKey authentication failed");
            }

            List<Claim> claims = new List<Claim>();
            claims.Add(new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, key.UserId));
            claims.AddRange(Permission.ToPermissions(key.GetBlob().Permissions).Select(permission =>
                new Claim(GreenFieldConstants.ClaimTypes.Permission, permission.ToString())));
            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, GreenFieldConstants.AuthenticationType)),
                GreenFieldConstants.AuthenticationType));
        }
    }
}
