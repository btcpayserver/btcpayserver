using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.GlobalSearch;

public class GlobalSearchPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "GlobalSearch";
    public override string Identifier => "BTCPayServer.Plugins.GlobalSearch";
    public override string Name => "GlobalSearch";
    public override string Description => "Add global search feature to your server.";

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("global-nav", "/Plugins/GlobalSearch/Views/NavExtension.cshtml");
        services.AddSearchResultItemProvider<StaticSearchResultProvider>();
        services.AddSearchResultItemProvider<DefaultSearchResultProvider>();
        services.AddScoped<SearchResultItemProviders>();
    }
}
