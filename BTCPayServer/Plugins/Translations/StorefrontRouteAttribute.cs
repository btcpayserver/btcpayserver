using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Plugins.Translations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class StorefrontRouteAttribute() : TypeFilterAttribute(typeof(StoreLanguageFilter));

internal class StoreLanguageFilter(TranslationsAccessor translationsAccessor, StoreRepository storeRepository,
    RouteStoreResolver routeStoreResolver) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await routeStoreResolver.Resolve(context.RouteData) is RouteStoreResolver.Resolution.Resolved { StoreId: {} storeId } &&
            await storeRepository.FindStore(storeId) is { } store)
            await translationsAccessor.UseStoreLanguage(store.GetStoreBlob().StorefrontLanguage);
        await next();
    }
}
