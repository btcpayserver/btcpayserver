using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PayButton
{
    public class PayButtonPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.PayButton";
        public override string Name => "PayButton";
        public override string Description => "Easily-embeddable HTML and highly-customizable payment button.";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("PayButton/NavExtension", "header-nav"));
            base.Execute(services);
        }
    }
}
