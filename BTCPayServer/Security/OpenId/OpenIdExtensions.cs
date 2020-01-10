using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.Server;

namespace BTCPayServer.Security.OpenId
{
    public static class OpenIdExtensions
    {
        public static ImmutableHashSet<string> Restrict(this ImmutableArray<string> scopes, ClaimsPrincipal claimsPrincipal)
        {
            HashSet<string> restricted = new HashSet<string>();
            foreach (var scope in scopes)
            {
                if (scope == BTCPayScopes.ServerManagement && !claimsPrincipal.IsInRole(Roles.ServerAdmin))
                    continue;
                restricted.Add(scope);
            }
            return restricted.ToImmutableHashSet();
        }
        public static async Task<ClaimsPrincipal> CreateClaimsPrincipalAsync(OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            IdentityOptions identityOptions,
            SignInManager<ApplicationUser> signInManager,
            OpenIddictRequest request,
            ApplicationUser user)
        {
            var principal = await signInManager.CreateUserPrincipalAsync(user);
            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            {
                principal.SetScopes(request.GetScopes().Restrict(principal));
            }
            else if (request.IsAuthorizationCodeGrantType() &&
                     string.IsNullOrEmpty(principal.GetInternalAuthorizationId()))
            {
                var app = await applicationManager.FindByClientIdAsync(request.ClientId);
                var authorizationId = await IsUserAuthorized(authorizationManager, request, user.Id, app.Id);
                if (!string.IsNullOrEmpty(authorizationId))
                {
                    principal.SetInternalAuthorizationId(authorizationId);
                }
            }

            principal.SetDestinations(identityOptions);
            return principal;
        }

        public static void SetDestinations(this ClaimsPrincipal principal, IdentityOptions identityOptions)
        {
            foreach (var claim in principal.Claims)
            {
                claim.SetDestinations(GetDestinations(identityOptions, claim, principal));
            }
        }

        private static IEnumerable<string> GetDestinations(IdentityOptions identityOptions, Claim claim,
            ClaimsPrincipal principal)
        {
            switch (claim.Type)
            {
                case OpenIddictConstants.Claims.Name:
                case OpenIddictConstants.Claims.Email:
                    yield return OpenIddictConstants.Destinations.AccessToken;
                    yield break;
            }
        }

        public static async Task<string> IsUserAuthorized(
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            OpenIddictRequest request, string userId, string applicationId)
        {
            var authorizations = await authorizationManager.ListAsync(queryable =>
                    queryable.Where(authorization =>
                        authorization.Subject == userId &&
                        authorization.Application.Id == applicationId &&
                        authorization.Status == OpenIddictConstants.Statuses.Valid)).ToArrayAsync();

            if (authorizations.Length > 0)
            {
                var scopeTasks = authorizations.Select(authorization =>
                    (authorizationManager.GetScopesAsync(authorization).AsTask(), authorization.Id));
                await Task.WhenAll(scopeTasks.Select((tuple) => tuple.Item1));

                var authorizationsWithSufficientScopes = scopeTasks
                    .Select((tuple) => (Id: tuple.Id, Scopes: tuple.Item1.Result))
                    .Where((tuple) => !request.GetScopes().Except(tuple.Scopes).Any());

                if (authorizationsWithSufficientScopes.Any())
                {
                    return authorizationsWithSufficientScopes.First().Id;
                }
            }

            return null;
        }
    }
}
