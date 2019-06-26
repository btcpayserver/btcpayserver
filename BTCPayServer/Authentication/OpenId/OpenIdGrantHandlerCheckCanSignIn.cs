using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public abstract class
        OpenIdGrantHandlerCheckCanSignIn : BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequest>
    {
        private readonly UserManager<ApplicationUser> _userManager;

        protected OpenIdGrantHandlerCheckCanSignIn(SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions, UserManager<ApplicationUser> userManager) : base(signInManager,
            identityOptions)
        {
            _userManager = userManager;
        }

        protected abstract bool IsValid(OpenIdConnectRequest request);

        public override async Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleTokenRequest notification)
        {
            var request = notification.Context.Request;
            if (!IsValid(request))
            {
                // Allow other handlers to process the event.
                return OpenIddictServerEventState.Unhandled;
            }

            var scheme = notification.Context.Scheme.Name;
            var authenticateResult = (await notification.Context.HttpContext.AuthenticateAsync(scheme));


            var user = await _userManager.GetUserAsync(authenticateResult.Principal);
            if (user == null)
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The token is no longer valid.");
                // Don't allow other handlers to process the event.
                return OpenIddictServerEventState.Handled;
            }

            // Ensure the user is still allowed to sign in.
            if (!await _signInManager.CanSignInAsync(user))
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The user is no longer allowed to sign in.");
                // Don't allow other handlers to process the event.
                return OpenIddictServerEventState.Handled;
            }

            notification.Context.Validate(await CreateTicketAsync(request, user));
            // Don't allow other handlers to process the event.
            return OpenIddictServerEventState.Handled;
        }
    }
}
