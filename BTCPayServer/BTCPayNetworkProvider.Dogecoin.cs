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
        public void InitDogecoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("DOGE");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://dogechain.info/tx/{0}" : "https://dogechain.info/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "dogecoin",
                DefaultRateProvider = new CoinAverageRateProviderDescription("DOGE"),
                CryptoImagePath = "imlegacy/dogecoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("3'") : new KeyPath("1'")
            });
        }
    }
}
