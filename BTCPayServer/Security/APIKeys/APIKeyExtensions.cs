using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Security.APIKeys
{
    public static class APIKeyExtensions
    {
        public static bool GetAPIKey(this HttpContext httpContext, out StringValues apiKey)
        {
            return httpContext.Request.Headers.TryGetValue("X-APIKEY", out apiKey);
        }

        public static Task<StoreData[]> GetStores(this ClaimsPrincipal claimsPrincipal, UserManager<ApplicationUser> userManager ,StoreRepository storeRepository)
        {
            var permissions =
                claimsPrincipal.Claims.Where(claim => claim.Type == APIKeyConstants.ClaimTypes.Permissions)
                    .Select(claim => claim.Value).ToList();

            if (permissions.Contains(APIKeyConstants.Permissions.StoreManagement))
            {
                return storeRepository.GetStoresByUserId(userManager.GetUserId(claimsPrincipal));
            }

            var storeIds = APIKeyConstants.Permissions.ExtractStorePermissionsIds(permissions);
            return storeRepository.GetStoresByUserId(userManager.GetUserId(claimsPrincipal), storeIds);
        }
        
        public static AuthenticationBuilder AddAPIKeyAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddScheme<APIKeyAuthenticationOptions, APIKeyAuthenticationHandler>(AuthenticationSchemes.ApiKey,
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
                c.Type.Equals(APIKeyConstants.ClaimTypes.Permissions, StringComparison.InvariantCultureIgnoreCase)).Select(claim => claim.Value).ToArray();
        }
        public static bool HasPermissions(this AuthorizationHandlerContext context, params string[] scopes)
        {
            return scopes.All(s => context.User.HasClaim(c => c.Type.Equals(APIKeyConstants.ClaimTypes.Permissions, StringComparison.InvariantCultureIgnoreCase) && c.Value.Split(' ').Contains(s)));
        }
    }
}
