#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Security;

public class BuiltInPermissionScopeProvider(
    IEnumerable<BuiltInPermissionScopeProvider.IStoreScopeProvider> storeScopeProviders) : IPermissionScopeProvider
{
    /// <summary>
    /// Resolves the store scope for a request, typically using route data or request context.
    /// Implementations are discovered by DI so plugins can add store-scoped authorization logic.
    /// </summary>
    public interface IStoreScopeProvider
    {
        Task<string?> GetStoreId(AuthorizationHandlerContext authContext, ScopeProviderAuthorizationContext providerContext, RouteData routeData);
    }

    /// <summary>
    /// Defines a route value name and the SQL used to look up its associated store id.
    /// Example: route value "invoiceId" -> query returning the owning store id.
    /// </summary>
    public record RouteValueToStoreIdQuery(string RouteValue, string Sql);

    internal class SqlStoreScopeProvider(RouteStoreResolver routeStoreResolver) : IStoreScopeProvider
    {
        public async Task<string?> GetStoreId(AuthorizationHandlerContext authContext, ScopeProviderAuthorizationContext providerContext, RouteData routeData)
        {

            var httpContext = providerContext.HttpContext;
            var storeId = providerContext.HttpContext.GetCurrentStoreId();
            var resolvedStoreIds = new HashSet<string>();
            if (storeId is not null)
                resolvedStoreIds.Add(storeId);

            // 1. Check in the routeData
            {
                if (routeData.Values.TryGetValue("storeId", out var v) && v is string o)
                {
                    storeId ??= o;
                    resolvedStoreIds.Add(o);
                }
            }

            // 2. Check in the query string
            {
                if (httpContext.Request.Query.TryGetValue("storeId", out var sv))
                {
                    storeId ??= sv.FirstOrDefault();
                    foreach (var value in sv)
                    {
                        if (value is not null)
                            resolvedStoreIds.Add(value);
                    }
                }
            }

            // 3. Check in forms
            {
                if (httpContext.Request.HasFormContentType &&
                    (await httpContext.Request.ReadFormAsync()).TryGetValue("storeId", out var sv))
                {
                    storeId ??= sv.FirstOrDefault();
                    foreach (var value in sv)
                    {
                        if (value is not null)
                            resolvedStoreIds.Add(value);
                    }
                }
            }

            // 4. Checks in walletId
            {
                if (routeData.Values.TryGetValue("walletId", out var walletId))
                {
                    if (WalletId.TryParse(walletId as string ?? "", out var w))
                    {
                        storeId ??= w.StoreId;
                        resolvedStoreIds.Add(w.StoreId);
                    }
                    else
                    {
                        authContext.Fail();
                        return null;
                    }
                }
            }

            if (resolvedStoreIds.Count > 1)
            {
                authContext.Fail();
                return null;
            }

            switch (await routeStoreResolver.Resolve(routeData))
            {
                case RouteStoreResolver.Resolution.Invalid:
                    authContext.Fail();
                    return null;
                case RouteStoreResolver.Resolution.Resolved resolved:
                    if (storeId is not null && resolved.StoreId != storeId)
                    {
                        authContext.Fail();
                        return null;
                    }
                    storeId = resolved.StoreId;
                    providerContext.HttpContext.Items[AdditionalScopeKey] = resolved.AdditionalScopes;
                    break;
                case RouteStoreResolver.Resolution.Unscoped:
                    providerContext.HttpContext.Items[AdditionalScopeKey] = Array.Empty<RouteStoreResolver.AdditionalScope>();
                    break;
            }
            return storeId;
        }
    }

    public const string AdditionalScopeKey = "BuiltInPermissionScopeProvider-AdditionalScope";
    public async Task<string?> GetScope(AuthorizationHandlerContext authContext, ScopeProviderAuthorizationContext providerContext)
    {
        var type = Permission.TryGetPolicyType(providerContext.Requirement.Policy);
        if (type is PolicyType.Store)
        {
            string? storeId = null;
            foreach (var provider in storeScopeProviders)
            {
                storeId = await provider.GetStoreId(authContext, providerContext, providerContext.HttpContext.GetRouteData());
            }
            return storeId;
        }

        return null;
    }
}
