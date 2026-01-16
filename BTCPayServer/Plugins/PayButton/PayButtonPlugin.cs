using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PayButton
{
    public class PayButtonPlugin : BaseBTCPayServerPlugin
    {
        public const string Area = "PayButton";
        public override string Identifier => "BTCPayServer.Plugins.PayButton";
        public override string Name => "Pay Button";
        public override string Description => "Easily-embeddable HTML button for accepting tips and donations .";

        public override void Execute(IServiceCollection services)
        {
            services.AddUIExtension("header-nav", "/Plugins/PayButton/Views/NavExtension.cshtml");
            base.Execute(services);
        }
    }
}
