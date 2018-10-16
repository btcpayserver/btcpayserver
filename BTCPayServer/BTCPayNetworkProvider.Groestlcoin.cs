using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitGroestlcoin()
        {

            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("GRS");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Groestlcoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://chainz.cryptoid.info/grs/tx.dws?{0}.htm" : "https://chainz.cryptoid.info/grs-test/tx.dws?{0}.htm",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "groestlcoin",
                DefaultRateRules = new[]
                {
                                "GRS_X = GRS_BTC * BTC_X",
                                "GRS_BTC = bittrex(GRS_BTC)"
                },
                CryptoImagePath = "imlegacy/groestlcoin.png",
                LightningImagePath = "imlegacy/groestlcoin-lightning.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("17'") : new KeyPath("1'")
            });
        }
    }
}
