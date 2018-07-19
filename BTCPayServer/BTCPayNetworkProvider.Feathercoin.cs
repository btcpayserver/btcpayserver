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
        public void InitFeathercoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("FTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Feathercoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://explorer.feathercoin.com/tx/{0}" : "https://explorer.feathercoin.com/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "feathercoin",
                DefaultRateRules = new[] 
                {
                                "FTC_X = FTC_BTC * BTC_X",
                                "FTC_BTC = bittrex(FTC_BTC)"
                },
                CryptoImagePath = "imlegacy/feathercoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("8'") : new KeyPath("1'")
            });
        }
    }
}
