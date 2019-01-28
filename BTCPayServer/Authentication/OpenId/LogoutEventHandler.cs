using System;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{

    public class LogoutEventHandler: BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleLogoutRequest>
    {
        public LogoutEventHandler(SignInManager<ApplicationUser> signInManager, IOptions<IdentityOptions> identityOptions) : base(signInManager, identityOptions)
        {
        }

        public override async Task<OpenIddictServerEventState> HandleAsync(OpenIddictServerEvents.HandleLogoutRequest notification)
        {
            // Ask ASP.NET Core Identity to delete the local and external cookies created
            // when the user agent is redirected from the external identity provider
            // after a successful authentication flow (e.g Google or Facebook).
            await _signInManager.SignOutAsync();

            // Returning a SignOutResult will ask OpenIddict to redirect the user agent
            // to the post_logout_redirect_uri specified by the client application.
            await notification.Context.HttpContext.SignOutAsync(OpenIddictServerDefaults.AuthenticationScheme);
            notification.Context.HandleResponse();
            return OpenIddictServerEventState.Handled;
        }
    }
}
