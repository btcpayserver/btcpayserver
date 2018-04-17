using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitPolis()
        {
            NBitcoin.Altcoins.Polis.EnsureRegistered();

            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("POLIS");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                BlockExplorerLink = NBXplorerNetworkProvider.ChainType == ChainType.Main ? "https://insight.polispay.org/tx/{0}" : "https://insight.polispay.org/tx/f{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "polis",
                DefaultRateProvider = new CoinAverageRateProviderDescription("POLIS"),
                CryptoImagePath = "imlegacy/polis.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NBXplorerNetworkProvider.ChainType),
                CoinType = NBXplorerNetworkProvider.ChainType == ChainType.Main ? new KeyPath("2'") : new KeyPath("1'")
            });
        }
    }
}
