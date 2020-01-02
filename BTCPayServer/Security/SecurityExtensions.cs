using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Routing;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security
{
    public static class SecurityExtensions
    {
        public static bool HasScopes(this AuthorizationHandlerContext context, params string[] scopes)
        {
            return scopes.All(s => context.User.HasClaim(c => c.Type == OpenIddictConstants.Claims.Scope && c.Value.Split(' ').Contains(s)));
        }
        public static string GetImplicitStoreId(this HttpContext httpContext)
        {
            // 1. Check in the routeData
            var routeData = httpContext.GetRouteData();
            string storeId = null;
            if (routeData != null)
            {
                if (routeData.Values.TryGetValue("storeId", out var v))
                    storeId = v as string;
            }

            if (storeId == null)
            {
                if (httpContext.Request.Query.TryGetValue("storeId", out var sv))
                {
                    storeId = sv.FirstOrDefault();
                }
            }

            // 2. Check in forms
            if (storeId == null)
            {
                if (httpContext.Request.HasFormContentType &&
                    httpContext.Request.Form != null &&
                    httpContext.Request.Form.TryGetValue("storeId", out var sv))
                {
                    storeId = sv.FirstOrDefault();
                }
            }

            // 3. Checks in walletId
            if (storeId == null && routeData != null)
            {
                if (routeData.Values.TryGetValue("walletId", out var walletId) &&
                    WalletId.TryParse((string)walletId, out var w))
                {
                    storeId = w.StoreId;
                }
            }

            return storeId;
        }
    }
}
