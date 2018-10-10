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
        public void InitUfo()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("UFO");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Ufo",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://chainz.cryptoid.info/ufo/tx.dws?{0}" : "https://chainz.cryptoid.info/ufo/tx.dws?{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "ufo",
                DefaultRateRules = new[] 
                {
                                "UFO_X = UFO_BTC * BTC_X",
                                "UFO_BTC = coinexchange(UFO_BTC)"
                },
                CryptoImagePath = "imlegacy/ufo.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("202'") : new KeyPath("1'")
            });
        }
    }
}
