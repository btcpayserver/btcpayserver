using System.Security.Claims;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
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
    public class ClientCredentialsGrantTypeEventHandler : BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequest>
    {
        private readonly OpenIddictApplicationManager<OpenIddictApplication> _applicationManager;

        public ClientCredentialsGrantTypeEventHandler(SignInManager<ApplicationUser> signInManager, OpenIddictApplicationManager<OpenIddictApplication> applicationManager,
            IOptions<IdentityOptions> identityOptions) : base(signInManager,
            identityOptions)
        {
            _applicationManager = applicationManager;
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
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId, notification.Context.HttpContext.RequestAborted);
            if (application == null)
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidClient,
                    description: "The client application was not found in the database.");
                // Don't allow other handlers to process the event.
                return OpenIddictServerEventState.Handled;
            }
            
            notification.Context.Validate(CreateTicket(request, application));
            // Don't allow other handlers to process the event.
            return OpenIddictServerEventState.Handled;
        }
        
        private AuthenticationTicket CreateTicket(OpenIdConnectRequest request,OpenIddictApplication application)
        {
            // Create a new ClaimsIdentity containing the claims that
            // will be used to create an id_token, a token or a code.
            var identity = new ClaimsIdentity(
                OpenIddictServerDefaults.AuthenticationScheme,
                OpenIdConnectConstants.Claims.Name,
                OpenIdConnectConstants.Claims.Role);

            // Use the client_id as the subject identifier.
            identity.AddClaim(OpenIdConnectConstants.Claims.Subject, application.ClientId,
                OpenIdConnectConstants.Destinations.AccessToken,
                OpenIdConnectConstants.Destinations.IdentityToken);

            identity.AddClaim(OpenIdConnectConstants.Claims.Name, application.DisplayName,
                OpenIdConnectConstants.Destinations.AccessToken,
                OpenIdConnectConstants.Destinations.IdentityToken);

            // Create a new authentication ticket holding the user identity.
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                new AuthenticationProperties(),
                OpenIddictServerDefaults.AuthenticationScheme);

            
            ticket.SetScopes(request.GetScopes());

            return ticket;
        }
    }
}