using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.GlobalSearch.Views;

namespace BTCPayServer.Plugins.GlobalSearch;

public class StaticSearchResultProvider(IEnumerable<ResultItemViewModel> items) : ISearchResultItemProvider
{
    public Task ProvideAsync(SearchResultItemProviderContext context)
    {
        context.ItemResults.AddRange(items);
        return Task.CompletedTask;
    }
}
