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
        public void InitZclassic()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("ZCL");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Zclassic",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "http://explorer.zclmine.pro/tx/{0}" : "http://testnet-explorer.zclmine.pro/tx/{0}"
                // Eventually - "https://explorer.zclassic.org/tx/{0}" : "https://testnet-explorer.zclassic.org/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "zclassic",
                DefaultRateRules = new[]
                {
                                "ZCL_X = ZCL_BTC * BTC_X",
                                "ZCL_BTC = bittrex(ZCL_BTC)"
                },
                CryptoImagePath = "imlegacy/zclassic.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("147'") : new KeyPath("1'")
            });
        }
    }
}
