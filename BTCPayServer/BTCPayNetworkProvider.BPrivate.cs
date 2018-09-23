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
        public void InitBPrivate()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("BTCP");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "BPrivate",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://explorer.btcprivate.org/tx/{0}" : "https://testnet-explorer.btcprivate.org/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoinprivate",
                DefaultRateRules = new[]
                {
                                "BTCP_X = BTCP_BTC * BTC_X",
                                "BTCP_BTC = hitbtc(BTCP_BTC)"
                },
                CryptoImagePath = "imlegacy/bprivate.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("183'") : new KeyPath("1'")
            });
        }
    }
}
