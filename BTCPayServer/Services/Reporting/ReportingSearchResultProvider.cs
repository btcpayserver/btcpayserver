#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Services.Reporting;

public class ReportingSearchResultProvider(IEnumerable<ReportProvider> reportProviders, IStringLocalizer stringLocalizer) : ISearchResultItemProvider
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;
    const string Category = "Reports";
    public Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.Store is null)
            return Task.CompletedTask;
        if (context.UserQuery is not null)
            return Task.CompletedTask;

        context.ItemResults.Add(new ResultItemViewModel()
        {
            RequiredPolicy = Policies.CanViewReports,
            Category = Category,
            Title = "Reports",
            Keywords = ["Reports"],
            Url = context.Url.Action(nameof(UIReportsController.StoreReports), "UIReports", new { storeId = context.Store!.Id })
        });

        var prefix = StringLocalizer["Reports"].Value + " ❯ ";
        context.ItemResults.AddRange(reportProviders.Select(provider => new ResultItemViewModel()
        {
            RequiredPolicy = Policies.CanViewReports,
            Category = Category,
            Url = context.Url.Action(nameof(UIReportsController.StoreReports), "UIReports", new { storeId = context.Store!.Id, viewName = provider.Name }),
            Keywords = ["Reports", provider.Name],
            Title = prefix + provider.Name
        }));
        return Task.CompletedTask;
    }
}
