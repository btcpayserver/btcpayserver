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
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://dogechain.info/tx/{0}" : "https://dogechain.info/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                DefaultRateRules = new[]
                {
                                "DOGE_X = DOGE_BTC * BTC_X",
                                "DOGE_BTC = bittrex(DOGE_BTC)"
                },
                CryptoImagePath = "imlegacy/dogecoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("3'") : new KeyPath("1'")
            });
        }
    }
}
