using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Tax;

public class TaxPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Tax";
    public override string Identifier => "BTCPayServer.Plugins.Tax";
    public override string Name => "Store Tax Rates";
    public override string Description => "Store-wide tax rates that apps can individually opt into.";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<TaxRateResolver>();
        services.AddUIExtension("header-nav", "/Plugins/Tax/Views/NavExtension.cshtml");

        base.Execute(services);
    }
}
