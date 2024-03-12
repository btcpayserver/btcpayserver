using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Altcoins
{
    public partial class AltcoinsPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Altcoins";
        public override string Name => "Altcoins";
        public override string Description => "Add altcoins support";

        public ChainName ChainName { get; private set; }
        public NBXplorerNetworkProvider NBXplorerNetworkProvider { get; private set; }
        public override void Execute(IServiceCollection applicationBuilder)
        {
            var services = (PluginServiceCollection)applicationBuilder;
            NBXplorerNetworkProvider = services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
            ChainName = NBXplorerNetworkProvider.NetworkType;

            var selectedChains = services.BootstrapServices.GetRequiredService<SelectedChains>();

            if (selectedChains.Contains("LTC"))
                InitLitecoin(services);
            if (selectedChains.Contains("DOGE"))
                InitDogecoin(services);
            if (selectedChains.Contains("BTG"))
                InitBGold(services);
            if (selectedChains.Contains("MONA"))
                InitMonacoin(services);
            if (selectedChains.Contains("DASH"))
                InitDash(services);
            if (selectedChains.Contains("GRS"))
                InitGroestlcoin(services);
        }
    }
}
