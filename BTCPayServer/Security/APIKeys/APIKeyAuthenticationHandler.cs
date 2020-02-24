using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security.APIKeys
{
    public class APIKeyAuthenticationHandler : AuthenticationHandler<APIKeyAuthenticationOptions>
    {
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;

        public APIKeyAuthenticationHandler(
            APIKeyRepository apiKeyRepository,
            IOptionsMonitor<IdentityOptions> identityOptions,
            IOptionsMonitor<APIKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
            _apiKeyRepository = apiKeyRepository;
            _identityOptions = identityOptions;
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
            claims.AddRange(key.GetPermissions()
                .Select(permission => new Claim(APIKeyConstants.ClaimTypes.Permissions, permission)));

            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, APIKeyConstants.AuthenticationType)), APIKeyConstants.AuthenticationType));
        }
    }
}
