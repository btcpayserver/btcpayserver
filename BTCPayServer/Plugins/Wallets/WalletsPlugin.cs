using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Wallets;

public class WalletsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Wallets";

    public override string Identifier => "BTCPayServer.Plugins.Wallets";
    public override string Name => "Wallets";
    public override string Description => "Pluginized wallet UI surface and setup flows.";

    public override void Execute(IServiceCollection services)
    {
    }
}
