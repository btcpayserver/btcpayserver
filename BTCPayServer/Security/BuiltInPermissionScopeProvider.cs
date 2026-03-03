#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

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

    internal class SqlStoreScopeProvider(
        IEnumerable<RouteValueToStoreIdQuery> routeDataToStoreIds,
        ApplicationDbContextFactory dbContextFactory,
        IMemoryCache memoryCache) : IStoreScopeProvider
    {
        public async Task<string?> GetStoreId(AuthorizationHandlerContext authContext, ScopeProviderAuthorizationContext providerContext, RouteData routeData)
        {
            await using var ctx = dbContextFactory.CreateContext();
            var storeId = providerContext.HttpContext.GetImplicitStoreId();
            List<AdditionalScope> additionalScopes = new();
            foreach (var i in routeDataToStoreIds)
            {
                if (routeData.Values.TryGetValue(i.RouteValue, out var ido) && ido is string id)
                {
                    var cacheKey = $"SqlStoreScopeProvider-{i.RouteValue}-{id}";
                    memoryCache.TryGetValue(cacheKey, out var storeIdFromCache);
                    var storeId2 = storeIdFromCache as string;
                    if (storeId2 is null)
                    {
                        storeId2 = await ctx.Database.GetDbConnection().ExecuteScalarAsync<string>(i.Sql, new { id });
                        if (storeId2 is not null)
                        {
                            var id2 = storeId2;
                            memoryCache.GetOrCreate(cacheKey, cacheEntry =>
                            {
                                cacheEntry.SlidingExpiration = TimeSpan.FromMinutes(10);
                                return id2;
                            });
                        }
                    }
                    storeId ??= storeId2;

                    // Consider the route /stores/{storeId}/apps/{appId}
                    // This check is making sure that the `storeId` is matching the scope resolved from `appId`.
                    if (storeId2 != storeId)
                        storeId2 = null;
                    if (storeId2 is not null)
                        additionalScopes.Add(new AdditionalScope(i.RouteValue, id));
                }
            }
            providerContext.HttpContext.Items[AdditionalScopeKey] = additionalScopes;
            return storeId;
        }
    }

    internal record AdditionalScope(string ScopeName, string Scope);

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
