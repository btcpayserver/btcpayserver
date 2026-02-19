#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.GlobalSearch;

public class SearchResultItemProviders(
    IEnumerable<ISearchResultItemProvider> providers,
    IAuthorizationService authorizationService,
    IStringLocalizer stringLocalizer,
    IHttpContextAccessor httpContextAccessor,
    UserManager<ApplicationUser> userManager)
{
    public async Task<GlobalSearchViewModel> GetViewModel(ClaimsPrincipal user, StoreData? store, IUrlHelper url)
    {
        if (store is null)
        {
            await authorizationService.AuthorizeAsync(user, null, new PolicyRequirement(Policies.CanViewStoreSettings));
            store = httpContextAccessor.HttpContext.GetStoreData();
        }

        var id = userManager.GetUserId(user) ?? throw new InvalidOperationException("Invalid user");
        var ctx = new SearchResultItemProviderContext(user, id, url, authorizationService)
        {
            Store = store
        };
        foreach (var provider in providers)
        {
            await provider.ProvideAsync(ctx);
        }

        Translate(ctx);
        return new GlobalSearchViewModel()
        {
            Items = ctx.ItemResults,
            StoreId = store?.Id,
            SearchUrl = url.Action("Global", "UISearch", new { area = GlobalSearchPlugin.Area })
        };
    }

    private void Translate(SearchResultItemProviderContext ctx)
    {
        foreach (var result in ctx.ItemResults)
        {
            if (result.Title is not null)
                result.Title = stringLocalizer[result.Title];
            if (result.SubTitle is not null)
                result.SubTitle = stringLocalizer[result.SubTitle];
            if (result.Category is not null)
                result.Category = stringLocalizer[result.Category];
            if (result.Keywords is not null)
            {
                for (int i = 0; i < result.Keywords.Length; i++)
                {
                    result.Keywords[i] = stringLocalizer[result.Keywords[i]];
                }
            }
        }
    }
}
