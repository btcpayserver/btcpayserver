using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitFeathercoin(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("FTC");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Feathercoin",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? "https://explorer.feathercoin.com/tx/{0}" : "https://explorer.feathercoin.com/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "feathercoin",
                DefaultRateRules = new[] 
                {
                                "FTC_X = FTC_BTC * BTC_X",
                                "FTC_BTC = bittrex(FTC_BTC)"
                },
                CryptoImagePath = "imlegacy/feathercoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("8'") : new KeyPath("1'")
            };
        }
    }
}
