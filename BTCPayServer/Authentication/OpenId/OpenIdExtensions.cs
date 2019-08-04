using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public static class OpenIdExtensions
    {
         public static async Task<AuthenticationTicket> CreateAuthenticationTicket(IdentityOptions identityOptions, SignInManager<ApplicationUser> signInManager ,OpenIdConnectRequest request, ApplicationUser user,
            AuthenticationProperties properties= null)
        {
            // Create a new ClaimsPrincipal containing the claims that
            // will be used to create an id_token, a token or a code.
            var principal = await signInManager.CreateUserPrincipalAsync(user);

            // Create a new authentication ticket holding the user identity.
            var ticket = new AuthenticationTicket(principal, properties,
                OpenIddictServerDefaults.AuthenticationScheme);

            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            {
                ticket.SetScopes(request.GetScopes());
            }

            foreach (var claim in ticket.Principal.Claims)
            {
                claim.SetDestinations(GetDestinations(identityOptions,claim, ticket));
            }

            return ticket;
        }

        private static IEnumerable<string> GetDestinations(IdentityOptions identityOptions,Claim claim, AuthenticationTicket ticket)
        {
            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.


            switch (claim.Type)
            {
                case OpenIddictConstants.Claims.Name:
                    yield return OpenIddictConstants.Destinations.AccessToken;

                    if (ticket.HasScope(OpenIddictConstants.Scopes.Profile))
                        yield return OpenIddictConstants.Destinations.IdentityToken;

                    yield break;

                case OpenIddictConstants.Claims.Email:
                    yield return OpenIddictConstants.Destinations.AccessToken;

                    if (ticket.HasScope(OpenIddictConstants.Scopes.Email))
                        yield return OpenIddictConstants.Destinations.IdentityToken;

                    yield break;

                case OpenIddictConstants.Claims.Role:
                    yield return OpenIddictConstants.Destinations.AccessToken;

                    if (ticket.HasScope(OpenIddictConstants.Scopes.Roles))
                        yield return OpenIddictConstants.Destinations.IdentityToken;

                    yield break;
                default:
                    if (claim.Type == identityOptions.ClaimsIdentity.SecurityStampClaimType)
                    {
                        // Never include the security stamp in the access and identity tokens, as it's a secret value.
                        yield break;
                    }
                    else
                    {
                        yield return OpenIddictConstants.Destinations.AccessToken;
                        yield break;
                    }
            }
        }
    }
}
