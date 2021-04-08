using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Shopify
{
    public class ShopifyPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Shopify";
        public override string Name => "Shopify";
        public override string Description => "Allows you to integrate BTCPay Server as a payment option in Shopify.";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            applicationBuilder.AddSingleton<IHostedService, ShopifyOrderMarkerHostedService>();
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("Shopify/StoreIntegrationShopifyOption",
                "store-integrations-list"));
            base.Execute(applicationBuilder);
        }
    }
}
