using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PayButton
{
    public class PointOfSalePlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.PointOfSale";
        public override string Name => "Point Of Sale";
        public override string Description => "Readily accept bitcoin without fees or a third-party, directly to your wallet.";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("PointOfSale/NavExtension", "apps-nav"));
            base.Execute(services);
        }
    }
}
