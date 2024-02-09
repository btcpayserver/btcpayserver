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

namespace BTCPayServer.Plugins.BoltcardFactory
{
    public class BoltcardFactoryPlugin : BaseBTCPayServerPlugin
    {
        public const string ViewsDirectory = "/Plugins/BoltcardFactory/Views";
        public const string AppType = "BoltcardFactory";

        public override string Identifier => "BTCPayServer.Plugins.BoltcardFactory";
        public override string Name => "BoltcardFactory";
        public override string Description => "Allow the creation of a consequential number of Boltcards in an efficient way";


        internal class BoltcardFactoryAppType : AppBaseType
        {
            private readonly LinkGenerator _linkGenerator;
            private readonly IOptions<BTCPayServerOptions> _btcPayServerOptions;

            public BoltcardFactoryAppType(
                LinkGenerator linkGenerator,
                IOptions<BTCPayServerOptions> btcPayServerOptions)
            {
                Type = AppType;
                Description = "Boltcard Factories";
                _linkGenerator = linkGenerator;
                _btcPayServerOptions = btcPayServerOptions;
            }
            public override Task<string> ConfigureLink(AppData app)
            {
                return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UIBoltcardFactoryController.UpdateBoltcardFactory),
                "UIBoltcardFactory", new { appId = app.Id }, _btcPayServerOptions.Value.RootPath)!);
            }

            public override Task<object?> GetInfo(AppData appData)
            {
                return Task.FromResult<object?>(null);
            }

            public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
            {
                appData.SetSettings(new CreatePullPaymentRequest());
                return Task.CompletedTask;
            }

            public override Task<string> ViewLink(AppData app)
            {
                return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UIBoltcardFactoryController.ViewBoltcardFactory),
                    "UIBoltcardFactory", new { appId = app.Id }, _btcPayServerOptions.Value.RootPath)!);
            }
        }
        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension($"{ViewsDirectory}/NavExtension.cshtml", "header-nav"));
            services.AddSingleton<AppBaseType, BoltcardFactoryAppType>();
            base.Execute(services);
        }
    }
}
