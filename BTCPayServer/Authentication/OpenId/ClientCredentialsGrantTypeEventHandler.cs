using System.Security.Claims;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Authentication.OpenId.Models;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public class
        ClientCredentialsGrantTypeEventHandler : BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequest>
    {
        private readonly OpenIddictApplicationManager<BTCPayOpenIdClient> _applicationManager;

        private readonly UserManager<ApplicationUser> _userManager;

        public ClientCredentialsGrantTypeEventHandler(SignInManager<ApplicationUser> signInManager,
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            IOptions<IdentityOptions> identityOptions, UserManager<ApplicationUser> userManager) : base(signInManager,
            identityOptions)
        {
            _applicationManager = applicationManager;
            _userManager = userManager;
        }

        public override async Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleTokenRequest notification)
        {
            var request = notification.Context.Request;
            if (!request.IsClientCredentialsGrantType())
            {
                // Allow other handlers to process the event.
                return OpenIddictServerEventState.Unhandled;
            }

            var application = await _applicationManager.FindByClientIdAsync(request.ClientId,
                notification.Context.HttpContext.RequestAborted);
            if (application == null)
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidClient,
                    description: "The client application was not found in the database.");
                // Don't allow other handlers to process the event.
                return OpenIddictServerEventState.Handled;
            }

            var user = await _userManager.FindByIdAsync(application.ApplicationUserId);

            notification.Context.Validate(await CreateTicketAsync(request, user));
            // Don't allow other handlers to process the event.
            return OpenIddictServerEventState.Handled;
        }
    }
}
