using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Services.Apps;
using Microsoft.Extensions.DependencyInjection;
using static BTCPayServer.Plugins.BoltcardFactory.BoltcardFactoryPlugin;

namespace BTCPayServer.Plugins.BoltcardTopUp;

public class BoltcardTopUpPlugin : BaseBTCPayServerPlugin
{
    public const string ViewsDirectory = "/Plugins/BoltcardTopUp/Views";
    public override string Identifier => "BTCPayServer.Plugins.BoltcardTopUp";
    public override string Name => "BoltcardTopUp";
    public override string Description => "Add the ability to Top-Up a Boltcard";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<IUIExtension>(new UIExtension($"{ViewsDirectory}/NavExtension.cshtml", "header-nav"));
        base.Execute(services);
    }
}
