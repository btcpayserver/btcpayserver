using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Security.Greenfield
{
    public class BasicAuthenticationHandler(
        IOptionsMonitor<IdentityOptions> identityOptions,
        IOptionsMonitor<GreenfieldAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SignInManager<ApplicationUser> signInManager,
        UserService userService,
        IRateLimitService rateLimitService,
        UserManager<ApplicationUser> userManager)
        : AuthenticationHandler<GreenfieldAuthenticationOptions>(options, logger, encoder)
    {
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
                    authHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[1].Trim();
                var decodedUsernamePassword =
                    Encoding.UTF8.GetString(Convert.FromBase64String(encodedUsernamePassword)).Split(':');
                username = decodedUsernamePassword[0];
                password = decodedUsernamePassword[1];
            }
            catch (Exception)
            {
                return Fail(
                    "Basic authentication header was not in a correct format. (username:password encoded in base64)");
            }

            var user = await userManager.Users
                .Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser =>
                    applicationUser.NormalizedUserName == userManager.NormalizeName(username));

            // We disable throttling for new accounts to give time to create API keys via greenfield API.
            if (user.Created is not {} created ||
                (DateTimeOffset.UtcNow - created) > TimeSpan.FromMinutes(5))
            {
                if (Context.Connection.RemoteIpAddress?.ToString() is string ip)
                    if (!await rateLimitService.Throttle(ZoneLimits.Login, ip))
                        return Fail($"Basic authentication failed: Rate limited. Please use authentication with API Keys to avoid throttling.");
            }

            var loggingContext = new UserService.CanLoginContext(user, baseUrl: Request.GetRequestBaseUrl());
            if (!await userService.CanLogin(loggingContext))
            {
                return Fail($"Basic authentication failed: {loggingContext.Failures[0].Text.Value}");
            }
            if (user.Fido2Credentials.Any())
            {
                return Fail("Cannot use Basic authentication when multi-factor is enabled.");
            }
            var result = await signInManager.CheckPasswordSignInAsync(user, password, true);
            if (!result.Succeeded)
                return Fail(result.ToString());
            var claims = new List<Claim>()
            {
                new Claim(identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, user.Id),
                new Claim(GreenfieldConstants.ClaimTypes.Permission,
                    Permission.Create(Policies.Unrestricted).ToString())
            };
            claims.AddRange((await userManager.GetRolesAsync(user)).Select(s => new Claim(identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, s)));

            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, GreenfieldConstants.AuthenticationType)),
                GreenfieldConstants.AuthenticationType));
        }

        AuthenticateResult Fail(string reason)
        {
            Context.Items.TryAdd(APIKeysAuthenticationHandler.AuthFailureReason, reason);
            return AuthenticateResult.Fail(reason);
        }
    }
}
