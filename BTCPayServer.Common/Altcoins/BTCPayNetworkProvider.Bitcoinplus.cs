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
        public void InitBitcoinplus()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("XBC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcoinplus",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://chainz.cryptoid.info/xbc/tx.dws?{0}" : "https://chainz.cryptoid.info/xbc/tx.dws?{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoinplus",
                DefaultRateRules = new[]
                {
                                "XBC_X = XBC_BTC * BTC_X",
                                "XBC_BTC = cryptopia(XBC_BTC)"
                },
                CryptoImagePath = "imlegacy/bitcoinplus.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("65'") : new KeyPath("1'")
            });
        }
    }
}
