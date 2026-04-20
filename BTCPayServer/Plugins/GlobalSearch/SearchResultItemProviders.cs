#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Plugins.GlobalSearch;

public class SearchResultItemProviders(
    IEnumerable<ISearchResultItemProvider> providers,
    IAuthorizationService authorizationService,
    IStringLocalizer stringLocalizer,
    UserManager<ApplicationUser> userManager)
{
    public async Task<GlobalSearchViewModel> GetViewModel(
        ClaimsPrincipal user,
        StoreData? store,
        IUrlHelper url,
        string? userQuery = null,
        int? maxResult = null,
        CancellationToken cancellationToken = default)
    {
        var id = userManager.GetUserId(user) ?? throw new InvalidOperationException("Invalid user");
        var ctx = new SearchResultItemProviderContext(user, id, url, authorizationService)
        {
            Store = store,
            UserQuery = userQuery,
            MaxResult = maxResult
        };
        foreach (var provider in providers)
        {
            await provider.ProvideAsync(ctx, cancellationToken);
        }

        await FilterAuthorizedItems(ctx);
        Translate(ctx);
        if (ctx.ItemResults.Count > maxResult)
            ctx.ItemResults = ctx.ItemResults.Take(maxResult.Value).ToList();
        return new GlobalSearchViewModel()
        {
            Items = ctx.ItemResults,
            StoreId = store?.Id,
            SearchUrl = url.Action("Global", "UISearch", new { area = GlobalSearchPlugin.Area })
        };
    }

    private static async Task FilterAuthorizedItems(SearchResultItemProviderContext ctx)
    {
        var authorizedItems = new List<ResultItemViewModel>();
        foreach (var item in ctx.ItemResults)
            if (item.RequiredPolicy is null || await ctx.IsAuthorized(item.RequiredPolicy))
                authorizedItems.Add(item);
        ctx.ItemResults = authorizedItems;
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
