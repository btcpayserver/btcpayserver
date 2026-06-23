#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Services;
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
    internal class TranslationProvider(
        IEnumerable<ResultItemViewModel> items,
        IEnumerable<ActionResultItemViewModel> routeItems) : IDefaultTranslationProvider
    {
        public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations()
        {
            HashSet<string> translations = new();
            foreach (var item in items)
            {
                if (item.Title is not null)
                    translations.Add(item.Title);
                if (item.Category is not null)
                    translations.Add(item.Category);
                if (item.Keywords is not null)
                    translations.AddRange(item.Keywords);
            }
            foreach (var item in routeItems)
            {
                translations.Add(item.Title);
                if (item.SubTitle is not null)
                    translations.Add(item.SubTitle);
                if (item.Category is not null)
                    translations.Add(item.Category);
                if (item.Keywords is not null)
                    translations.AddRange(item.Keywords);
            }
            return Task.FromResult(translations.Select(s => KeyValuePair.Create(s, null as string)).ToArray());
        }
    }

    public async Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null)
            return;
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
                Url = context.Url.Action(item.Action, item.Controller, item.Values?.Invoke(context))
            });
        }
    }
}
