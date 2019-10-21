using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitZCoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("XZC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "ZCoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://explorer.zcoin.io/tx/{0}" : "https://testexplorer.zcoin.io/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "zcoin",
                DefaultRateRules = new[]
                {
                                "XZC_X = XZC_BTC * BTC_X",
                                "XZC_BTC = binance(XZC_BTC)"
                },
                CryptoImagePath = "imlegacy/zcoin.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("136'") : new KeyPath("1'")
            });
        }
    }
}
