using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Contracts.BTCPayServer;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider: IBTCPayNetworkProvider
    {
        public IEnumerable<BTCPayNetworkBase> GetNetworks(NetworkType networkType)
        {
            
            var networks = new List<BTCPayNetworkBase>();
            var nbXplorerNetworkProvider = new NBXplorerNetworkProvider(networkType);
            networks.AddRange(new[]
            {
                InitLiquid(nbXplorerNetworkProvider),
                InitLitecoin(nbXplorerNetworkProvider), 
                InitBitcore(nbXplorerNetworkProvider), 
                InitDogecoin(nbXplorerNetworkProvider), 
                InitBitcoinGold(nbXplorerNetworkProvider), 
                InitMonacoin(nbXplorerNetworkProvider), 
                InitDash(nbXplorerNetworkProvider), 
                InitFeathercoin(nbXplorerNetworkProvider), 
                InitGroestlcoin(nbXplorerNetworkProvider), 
                InitViacoin(nbXplorerNetworkProvider), 
                InitMonero(networkType), 
                InitPolis(nbXplorerNetworkProvider), 
            });
            networks.AddRange(InitLiquidAssets(nbXplorerNetworkProvider));

            // Assume that electrum mappings are same as BTC if not specified
            foreach (var network in networks.OfType<BTCPayNetwork>())
            {
                if(network.ElectrumMapping.Count == 0)
                {
                    network.ElectrumMapping = BitcoinBTCPayNetworkProvider.GetElectrumMapping(networkType);
                    if (!network.NBitcoinNetwork.Consensus.SupportSegwit)
                    {
                        network.ElectrumMapping =
                            network.ElectrumMapping
                                .Where(kv => kv.Value == DerivationType.Legacy)
                                .ToDictionary(k => k.Key, k => k.Value);
                    }
                }
            }

            // Disabled because of https://twitter.com/Cryptopia_NZ/status/1085084168852291586
            //InitBitcoinplus();
            //InitUfo();
            return networks;
        }
    }
}
