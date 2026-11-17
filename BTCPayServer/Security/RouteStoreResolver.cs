#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Security;

public class RouteStoreResolver(
    IEnumerable<BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery> queries,
    ApplicationDbContextFactory dbContextFactory,
    IMemoryCache memoryCache)
{
    public abstract record Resolution
    {
        private Resolution() { }

        public sealed record Unscoped : Resolution;
        public sealed record Resolved(string StoreId, IReadOnlyList<AdditionalScope> AdditionalScopes) : Resolution;
        public sealed record Invalid : Resolution;
    }

    static readonly MemoryCacheEntryOptions CacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(10) };

    readonly BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery[] _queries =
        queries.ToDictionary(query => query.RouteValue, StringComparer.OrdinalIgnoreCase).Values.ToArray();

    public record AdditionalScope(string ScopeName, string Scope);

    private async Task<string?> FindStoreId(BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery query, string id)
    {
        var cacheKey = $"{nameof(RouteStoreResolver)}-{query.RouteValue}-{query.Sql}-{id}";
        if (memoryCache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached;

        await using var ctx = dbContextFactory.CreateContext();
        var storeId = await ctx.Database.GetDbConnection().ExecuteScalarAsync<string>(query.Sql, new { id });
        if (storeId is not null)
            memoryCache.Set(cacheKey, storeId, CacheOptions);
        return storeId;
    }

    public async Task<Resolution> Resolve(RouteData routeData)
    {
        var storeId = routeData.Values.TryGetValue("storeId", out var storeIdValue) ? storeIdValue as string : null;
        List<AdditionalScope> additionalScopes = new();
        foreach (var query in _queries)
        {
            if (!routeData.Values.TryGetValue(query.RouteValue, out var idValue) || idValue is not string id)
                continue;

            additionalScopes.Add(new AdditionalScope(query.RouteValue, id));
            var found = await FindStoreId(query, id);
            if (found is null || (storeId is not null && storeId != found))
                return new Resolution.Invalid();
            storeId ??= found;
        }
        return storeId is null ? new Resolution.Unscoped() : new Resolution.Resolved(storeId, additionalScopes);
    }
}
