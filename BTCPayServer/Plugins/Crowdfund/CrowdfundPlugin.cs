using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PayButton
{
    public class CrowdfundPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Crowdfund";
        public override string Name => "Crowdfund";
        public override string Description => "Create a self-hosted funding campaign, similar to Kickstarter or Indiegogo. Funds go directly to the creator’s wallet without any fees.";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("Crowdfund/NavExtension", "apps-nav"));
            base.Execute(services);
        }
    }
}
