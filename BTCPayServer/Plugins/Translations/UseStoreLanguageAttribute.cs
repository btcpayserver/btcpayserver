using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Dapper;

namespace BTCPayServer.Plugins.Translations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class UseStoreLanguageAttribute() : TypeFilterAttribute(typeof(StoreLanguageFilter));

internal class StoreLanguageFilter(LocalizerService localizer, StoreRepository storeRepository,
    ApplicationDbContextFactory dbContextFactory, IMemoryCache memoryCache,
    IEnumerable<BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery> routeValueQueries) : IAsyncActionFilter
{
    static readonly MemoryCacheEntryOptions StoreIdCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(15) };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var storeId = await FindStoreId(context.RouteData);
        if (storeId is not null && await storeRepository.FindStore(storeId) is { } store)
            await localizer.UseStoreLanguage(store.GetStoreBlob().StoreLanguage);
        await next();
    }

    async Task<string?> FindStoreId(RouteData routeData)
    {
        if (routeData.Values.TryGetValue("storeId", out var storeIdValue) && storeIdValue is string storeId)
            return storeId;

        foreach (var query in routeValueQueries)
        {
            if (!routeData.Values.TryGetValue(query.RouteValue, out var idValue) || idValue is not string id)
                continue;

            var cacheKey = $"{nameof(StoreLanguageFilter)}-{query.RouteValue}-{id}";
            if (memoryCache.TryGetValue(cacheKey, out string? cached) && cached is not null)
                return cached;

            await using var ctx = dbContextFactory.CreateContext();
            var found = await ctx.Database.GetDbConnection().ExecuteScalarAsync<string>(query.Sql, new { id });
            if (found is null)
                continue;
            memoryCache.Set(cacheKey, found, StoreIdCacheOptions);
            return found;
        }
        return null;
    }
}
