#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.BoltcardFactory.Controllers;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BoltcardBalance
{
    public class BoltcardBalancePlugin : BaseBTCPayServerPlugin
    {
        public const string ViewsDirectory = "/Plugins/BoltcardBalance/Views";
        public const string AppType = "BoltcardBalance";

        public override string Identifier => "BTCPayServer.Plugins.BoltcardBalance";
        public override string Name => "BoltcardBalance";
        public override string Description => "Add ability to check the history and balance of a Boltcard";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension($"{ViewsDirectory}/NavExtension.cshtml", "header-nav"));
            base.Execute(services);
        }
    }
}
