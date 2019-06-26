using System.Collections.Generic;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Models;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public class AuthorizationEventHandler : BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleAuthorizationRequest>
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public override async Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleAuthorizationRequest notification)
        {
            if (!notification.Context.Request.IsAuthorizationRequest())
            {
                return OpenIddictServerEventState.Unhandled;
            }

            var auth = await notification.Context.HttpContext.AuthenticateAsync();
            if (!auth.Succeeded)
            {
                // If the client application request promptless authentication,
                // return an error indicating that the user is not logged in.
                if (notification.Context.Request.HasPrompt(OpenIdConnectConstants.Prompts.None))
                {
                    var properties = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIdConnectConstants.Properties.Error] = OpenIdConnectConstants.Errors.LoginRequired,
                        [OpenIdConnectConstants.Properties.ErrorDescription] = "The user is not logged in."
                    });


                    // Ask OpenIddict to return a login_required error to the client application.
                    await notification.Context.HttpContext.ForbidAsync(properties);
                    notification.Context.HandleResponse();
                    return OpenIddictServerEventState.Handled;
                }

                await notification.Context.HttpContext.ChallengeAsync();
                notification.Context.HandleResponse();
                return OpenIddictServerEventState.Handled;
            }

            // Retrieve the profile of the logged in user.
            var user = await _userManager.GetUserAsync(auth.Principal);
            if (user == null)
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "An internal error has occurred");

                return OpenIddictServerEventState.Handled;
            }

            // Create a new authentication ticket.
            var ticket = await CreateTicketAsync(notification.Context.Request, user);

            // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
            notification.Context.Validate(ticket);
            return OpenIddictServerEventState.Handled;
        }

        public AuthorizationEventHandler(
            UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions) : base(signInManager, identityOptions)
        {
            _userManager = userManager;
        }
    }
}
