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
        public void InitPolis()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("POLIS");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://insight.polispay.org/api/tx/{0}" : "https://insight.polispay.org/apitx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "polis",
                DefaultRateRules = new[]
                {
                                "POLIS_X = POLIS_BTC * BTC_X",
                                "POLIS_BTC = cryptopia(POLIS_BTC)"
                },
                CryptoImagePath = "imlegacy/polis.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("3'") : new KeyPath("1'")
            });
        }
    }
}
