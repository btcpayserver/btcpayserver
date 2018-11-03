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
        public void InitBitcore()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("BTX");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcore",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://insight.bitcore.cc/tx/{0}" : "https://insight.bitcore.cc/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcore",
                DefaultRateRules = new[]
                {
                                "BTX_X = BTX_BTC * BTC_X",
                                "BTX_BTC = cryptopia(BTX_BTC)"
                },
                CryptoImagePath = "imlegacy/bitcore.svg",
                LightningImagePath = "imlegacy/bitcore-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("160'") : new KeyPath("1'")
            });
        }
    }
}
