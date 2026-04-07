#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.GlobalSearch;

public class ActionResultItemViewModel
{
    public string? RequiredPolicy { get; set; }
    public required string Title { get; set; }
    public string? SubTitle { get; set; }
    public required string Action { get; set; }
    public required string Controller { get; set; }
    public Func<SearchResultItemProviderContext, object>? Values { get; set; }
    public string? Category { get; set; }
    public string[]? Keywords { get; set; }
}

public class StaticSearchResultProvider(
    IEnumerable<ResultItemViewModel> items,
    IEnumerable<ActionResultItemViewModel> routeItems) : ISearchResultItemProvider
{
    public async Task ProvideAsync(SearchResultItemProviderContext context)
    {
        context.ItemResults.AddRange(items);

        foreach (var item in routeItems)
        {
            if (item.RequiredPolicy is not null && !await context.IsAuthorized(item.RequiredPolicy))
                continue;
            context.ItemResults.Add(new ResultItemViewModel()
            {
                RequiredPolicy = item.RequiredPolicy,
                Category = item.Category,
                Title = item.Title,
                Keywords = item.Keywords,
                Url = context.Url.Action(item.Action, item.Controller, item.Values?.Invoke(context)),
                SubTitle = item.SubTitle
            });
        }
    }
}
