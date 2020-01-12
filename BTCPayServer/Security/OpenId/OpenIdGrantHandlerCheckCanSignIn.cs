using System.Threading.Tasks;
using OpenIddict.Abstractions;
using BTCPayServer.Data;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Core;
using OpenIddict.Server;
using Microsoft.AspNetCore;
using OpenIddict.Server.AspNetCore;

namespace BTCPayServer.Security.OpenId
{
    public class OpenIdGrantHandlerCheckCanSignIn :
        BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
   OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.HandleTokenRequestContext>()
               .UseScopedHandler<OpenIdGrantHandlerCheckCanSignIn>()
               .Build();

        private readonly UserManager<ApplicationUser> _userManager;

        public OpenIdGrantHandlerCheckCanSignIn(
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions, UserManager<ApplicationUser> userManager) : base(
            applicationManager, authorizationManager, signInManager,
            identityOptions)
        {
            _userManager = userManager;
        }

        public override async ValueTask HandleAsync(
            OpenIddictServerEvents.HandleTokenRequestContext notification)
        {
            var request = notification.Request;
            if (!request.IsRefreshTokenGrantType() && !request.IsAuthorizationCodeGrantType())
            {
                // Allow other handlers to process the event.
                return;
            }

            var httpContext = notification.Transaction.GetHttpRequest().HttpContext;
            var authenticateResult = (await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

            var user = await _userManager.GetUserAsync(authenticateResult.Principal);
            if (user == null)
            {
                notification.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The token is no longer valid.");
                return;
            }

            // Ensure the user is still allowed to sign in.
            if (!await _signInManager.CanSignInAsync(user))
            {
                notification.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The user is no longer allowed to sign in.");
                return;
            }

            notification.Principal = await this.CreateClaimsPrincipalAsync(request, user);
        }
    }
}
