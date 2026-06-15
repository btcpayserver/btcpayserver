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
    /// <summary>
    /// Add a static search result, title, subtitle, category, keywords, will be exposed to translations.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="vm"></param>
    /// <returns></returns>
    public static IServiceCollection AddStaticSearch(this IServiceCollection services, ResultItemViewModel vm)
    {
        services.AddSingleton(vm);
        return services;
    }

    /// <summary>
    /// Add a static search result, title, subtitle, category, keywords, will be exposed to translations.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="actionResultItem"></param>
    /// <returns></returns>
    public static IServiceCollection AddStaticSearch(this IServiceCollection services, ActionResultItemViewModel actionResultItem)
    {
        services.AddSingleton(actionResultItem);
        return services;
    }
    /// <summary>
    /// Add a static search result, title, subtitle, category, keywords, will be exposed to translations.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="vm"></param>
    /// <returns></returns>
    public static IServiceCollection AddStaticSearch(this IServiceCollection services, ResultItemViewModel[] vm)
    {
        foreach (var v in vm)
            services.AddSingleton(v);
        return services;
    }

    /// <summary>
    /// Add a static search result, title, subtitle, category, keywords, will be exposed to translations.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="actionResultItem"></param>
    /// <returns></returns>
    public static IServiceCollection AddStaticSearch(this IServiceCollection services, ActionResultItemViewModel[] actionResultItem)
    {
        foreach (var v in actionResultItem)
            services.AddSingleton(v);
        return services;
    }
}
