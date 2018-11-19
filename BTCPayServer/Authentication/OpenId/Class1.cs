using System.Collections.Generic;
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
    public abstract class
        BaseOpenIdGrantHandler : IOpenIddictServerEventHandler<OpenIddictServerEvents.HandleTokenRequest>
    {
        protected readonly SignInManager<ApplicationUser> _signInManager;
        protected readonly IOptions<IdentityOptions> _identityOptions;

        protected BaseOpenIdGrantHandler(SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions)
        {
            _signInManager = signInManager;
            _identityOptions = identityOptions;
        }

        protected async Task<AuthenticationTicket> CreateTicketAsync(OpenIdConnectRequest request, ApplicationUser user,
            AuthenticationProperties properties = null)
        {
            // Create a new ClaimsPrincipal containing the claims that
            // will be used to create an id_token, a token or a code.
            var principal = await _signInManager.CreateUserPrincipalAsync(user);

            // Create a new authentication ticket holding the user identity.
            var ticket = new AuthenticationTicket(principal, properties,
                OpenIddictServerDefaults.AuthenticationScheme);


            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            {
                // Set the list of scopes granted to the client application.
                // Note: the offline_access scope must be granted
                // to allow OpenIddict to return a refresh token.
                ticket.SetScopes(request.GetScopes());
            }

            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.

            foreach (var claim in ticket.Principal.Claims)
            {
                // Never include the security stamp in the access and identity tokens, as it's a secret value.
                if (claim.Type == _identityOptions.Value.ClaimsIdentity.SecurityStampClaimType)
                {
                    continue;
                }

                var destinations = new List<string>
                {
                    OpenIdConnectConstants.Destinations.AccessToken
                };

                // Only add the iterated claim to the id_token if the corresponding scope was granted to the client application.
                // The other claims will only be added to the access_token, which is encrypted when using the default format.
                if ((claim.Type == OpenIdConnectConstants.Claims.Name &&
                     ticket.HasScope(OpenIdConnectConstants.Scopes.Profile)) ||
                    (claim.Type == OpenIdConnectConstants.Claims.Email &&
                     ticket.HasScope(OpenIdConnectConstants.Scopes.Email)) ||
                    (claim.Type == OpenIdConnectConstants.Claims.Role &&
                     ticket.HasScope(OpenIddictConstants.Claims.Roles)))
                {
                    destinations.Add(OpenIdConnectConstants.Destinations.IdentityToken);
                }

                claim.SetDestinations(destinations);
            }

            return ticket;
        }

        public abstract Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleTokenRequest notification);
    }

    public class PasswordGrantTypeEventHandler : BaseOpenIdGrantHandler
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public PasswordGrantTypeEventHandler(SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions) : base(signInManager, identityOptions)
        {
        }

        public override async Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleTokenRequest notification)
        {
            var request = notification.Context.Request;
            if (!request.IsPasswordGrantType())
            {
                // Allow other handlers to process the event.
                return OpenIddictServerEventState.Unhandled;
            }

            // Validate the user credentials.
            // Note: to mitigate brute force attacks, you SHOULD strongly consider
            // applying a key derivation function like PBKDF2 to slow down
            // the password validation process. You SHOULD also consider
            // using a time-constant comparer to prevent timing attacks.
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null ||
                !(await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true))
                    .Succeeded)
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The specified credentials are invalid.");
                // Don't allow other handlers to process the event.
                return OpenIddictServerEventState.Handled;
            }

            notification.Context.Validate(await CreateTicketAsync(request, user));
            // Don't allow other handlers to process the event.
            return OpenIddictServerEventState.Handled;
        }
    }

    public class AuthorizationCode_RefreshTokenGrantTypeEventHandler : BaseOpenIdGrantHandler
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public AuthorizationCode_RefreshTokenGrantTypeEventHandler(SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions, UserManager<ApplicationUser> userManager) : base(signInManager,
            identityOptions)
        {
            _userManager = userManager;
        }

        public override async Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleTokenRequest notification)
        {
            var request = notification.Context.Request;
            if (!request.IsRefreshTokenGrantType() && !request.IsAuthorizationCodeGrantType())
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



    public class ClientCredentialsGrantTypeEventHandler : BaseOpenIdGrantHandler
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
