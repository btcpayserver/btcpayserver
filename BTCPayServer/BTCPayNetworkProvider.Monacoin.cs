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
        public void InitMonacoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("MONA");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Monacoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://mona.insight.monaco-ex.org/insight/tx/{0}" : "https://testnet-mona.insight.monaco-ex.org/insight/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "monacoin",
                DefaultRateRules = new[] 
                {
                                "MONA_X = MONA_BTC * BTC_X",
                                "MONA_BTC = zaif(MONA_BTC)"
                },
                CryptoImagePath = "imlegacy/monacoin.png",
                LightningImagePath = "imlegacy/mona-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("22'") : new KeyPath("1'")
            });
        }
    }
}
