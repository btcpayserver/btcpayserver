using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitDogecoin(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("DOGE");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Dogecoin",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? "https://dogechain.info/tx/{0}" : "https://dogechain.info/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "dogecoin",
                DefaultRateRules = new[] 
                {
                                "DOGE_X = DOGE_BTC * BTC_X",
                                "DOGE_BTC = bittrex(DOGE_BTC)"
                },
                CryptoImagePath = "imlegacy/dogecoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("3'") : new KeyPath("1'")
            };
        }
    }
}
