using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer;

public static class GlobalSearchExtensions
{
    public static IServiceCollection AddSearchResultItemProvider<T>(this IServiceCollection services) where T : class, ISearchResultItemProvider
    {
        services.AddSingleton<ISearchResultItemProvider, T>();
        return services;
    }
    public static IServiceCollection AddSearchResultItemViewModel(this IServiceCollection services, ResultItemViewModel vm)
    {
        services.AddSingleton(vm);
        return services;
    }
}

