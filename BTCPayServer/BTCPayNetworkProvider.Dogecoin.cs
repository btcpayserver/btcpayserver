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
        public void InitDogecoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("DOGE");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Dogecoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://dogechain.info/tx/{0}" : "https://dogechain.info/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "dogecoin",
                DefaultRateRules = new[] 
                {
                                "DOGE_X = DOGE_BTC * BTC_X",
                                "DOGE_BTC = bittrex(DOGE_BTC)"
                },
                CryptoImagePath = "imlegacy/dogecoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("3'") : new KeyPath("1'"),
                MinFee = Money.Coins(1m)
            });
        }
    }
}
