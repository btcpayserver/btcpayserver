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
        public void InitLitecoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Litecoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://live.blockcypher.com/ltc/tx/{0}/" : "http://explorer.litecointools.com/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "litecoin",
                CryptoImagePath = "imlegacy/litecoin.svg",
                LightningImagePath = "imlegacy/litecoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("2'") : new KeyPath("1'")
            });
        }
    }
}
