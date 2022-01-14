using System;
using System.Linq;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Security.Greenfield
{
    public static class APIKeyExtensions
    {
        public static bool GetAPIKey(this HttpContext httpContext, out StringValues apiKey)
        {
            apiKey = default;
            if (httpContext.Request.Headers.TryGetValue("Authorization", out var value) &&
                value.ToString().StartsWith("token ", StringComparison.InvariantCultureIgnoreCase))
            {
                apiKey = value.ToString().Substring("token ".Length);
                return true;
            }
            return false;
        }

        public static AuthenticationBuilder AddAPIKeyAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddScheme<GreenfieldAuthenticationOptions, APIKeysAuthenticationHandler>(AuthenticationSchemes.GreenfieldAPIKeys,
                o => { });
            builder.AddScheme<GreenfieldAuthenticationOptions, BasicAuthenticationHandler>(AuthenticationSchemes.GreenfieldBasic,
                o => { });
            return builder;
        }

        public static IServiceCollection AddAPIKeyAuthentication(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<APIKeyRepository>();
            serviceCollection.AddScoped<IAuthorizationHandler, GreenfieldAuthorizationHandler>();
            serviceCollection.AddScoped<IAuthorizationHandler, LocalGreenfieldAuthorizationHandler>();
            return serviceCollection;
        }

        public static string[] GetPermissions(this AuthorizationHandlerContext context)
        {
            return context.User.Claims.Where(c =>
                    c.Type.Equals(GreenfieldConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase))
                .Select(claim => claim.Value).ToArray();
        }
        public static bool HasPermission(this AuthorizationHandlerContext context, Permission permission)
        {
            return HasPermission(context, permission, false);
        }
        public static bool HasPermission(this AuthorizationHandlerContext context, Permission permission, bool requireUnscoped)
        {
            foreach (var claim in context.User.Claims.Where(c =>
                c.Type.Equals(GreenfieldConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (Permission.TryParse(claim.Value, out var claimPermission))
                {
                    if (requireUnscoped && claimPermission.Scope is not null)
                        continue;
                    if (claimPermission.Contains(permission))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
