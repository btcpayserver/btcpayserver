#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security.Greenfield
{
    public class APIKeysAuthenticationHandler(
        APIKeyRepository apiKeyRepository,
        IOptionsMonitor<IdentityOptions> identityOptions,
        IOptionsMonitor<GreenfieldAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UserService userService,
        UserManager<ApplicationUser> userManager)
        : AuthenticationHandler<GreenfieldAuthenticationOptions>(options, logger, encoder)
    {
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // This one deserve some explanation...
            // Some routes have this authorization.
            // [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
            // This is meant for API routes that we wish to access by greenfield but also via the browser for documentation purpose (say /misc/rate-sources)
            // Now, if we aren't logged nor authenticated via greenfield, the AuthenticationHandlers get challenged.
            // The last handler to be challenged is the CookieAuthenticationHandler, which instruct to handle the challenge as a redirection to
            // the login page.
            // But this isn't what we want when we call the API programmatically, instead we want an error 401 with a json error message.
            // This hack modify a request's header to trick the CookieAuthenticationHandler to not do a redirection.
            if (!Request.Headers.Accept.Any(s => s != null && s.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)))
                Request.Headers.XRequestedWith = new Microsoft.Extensions.Primitives.StringValues("XMLHttpRequest");
            return base.HandleChallengeAsync(properties);
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Context.Request.HttpContext.GetAPIKey(out var apiKey) || string.IsNullOrEmpty(apiKey))
                return AuthenticateResult.NoResult();

            var key = await apiKeyRepository.GetKey(apiKey, true);
            var loggingContext = new UserService.CanLoginContext(key?.User, baseUrl: Request.GetRequestBaseUrl());
            if (!await userService.CanLogin(loggingContext) || key is null)
            {
                return AuthenticateResult.Fail($"ApiKey authentication failed: {loggingContext.Failures[0].Text.Value}");
            }

            var claims = new List<Claim> { new (identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, key.UserId) };
            claims.AddRange((await userManager.GetRolesAsync(key.User)).Select(s => new Claim(identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, s)));
            claims.AddRange(Permission.ToPermissions(key.GetBlob()?.Permissions ?? Array.Empty<string>()).Select(permission =>
                new Claim(GreenfieldConstants.ClaimTypes.Permission, permission.ToString())));
            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, GreenfieldConstants.AuthenticationType)),
                GreenfieldConstants.AuthenticationType));
        }
    }
}
