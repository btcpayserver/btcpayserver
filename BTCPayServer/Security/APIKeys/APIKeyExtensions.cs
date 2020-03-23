using System;
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Security.Bitpay;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Security.APIKeys
{
    public static class APIKeyExtensions
    {
        public static bool GetAPIKey(this HttpContext httpContext, out StringValues apiKey)
        {
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
            builder.AddScheme<APIKeyAuthenticationOptions, APIKeyAuthenticationHandler>(AuthenticationSchemes.Greenfield,
                o => { });
            return builder;
        }

        public static IServiceCollection AddAPIKeyAuthentication(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<APIKeyRepository>();
            serviceCollection.AddScoped<IAuthorizationHandler, APIKeyAuthorizationHandler>();
            return serviceCollection;
        }

        public static string[] GetPermissions(this AuthorizationHandlerContext context)
        {
            return context.User.Claims.Where(c =>
                    c.Type.Equals(APIKeyConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase))
                .Select(claim => claim.Value).ToArray();
        }

        public static bool HasPermission(this AuthorizationHandlerContext context, Permission permission)
        {
            foreach (var claim in context.User.Claims.Where(c =>
                c.Type.Equals(APIKeyConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (Permission.TryParse(claim.Value, out var claimPermission))
                {
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
