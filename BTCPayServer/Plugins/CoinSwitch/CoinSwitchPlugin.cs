using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.CoinSwitch
{
    public class CoinSwitchPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.CoinSwitch";
        public override string Name => "CoinSwitch";

        public override string Description =>
            "Allows you to embed a coinswitch conversion screen to allow customers to pay with altcoins.";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            applicationBuilder.AddSingleton<CoinSwitchService>();
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("CoinSwitch/StoreIntegrationCoinSwitchOption",
                "store-integrations-list"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("CoinSwitch/CheckoutContentExtension",
                "checkout-bitcoin-post-content"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("CoinSwitch/CheckoutContentExtension",
                "checkout-ethereum-post-content"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("CoinSwitch/CheckoutTabExtension",
                "checkout-bitcoin-post-tabs"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("CoinSwitch/CheckoutTabExtension",
                "checkout-ethereum-post-tabs"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("CoinSwitch/CheckoutEnd",
                "checkout-end"));
            base.Execute(applicationBuilder);
        }
    }
}
