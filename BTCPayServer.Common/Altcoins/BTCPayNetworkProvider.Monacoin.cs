using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitMonacoin(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("MONA");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Monacoin",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? "https://mona.insight.monaco-ex.org/insight/tx/{0}" : "https://testnet-mona.insight.monaco-ex.org/insight/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "monacoin",
                DefaultRateRules = new[] 
                {
                                "MONA_X = MONA_BTC * BTC_X",
                                "MONA_BTC = bittrex(MONA_BTC)"
                },
                CryptoImagePath = "imlegacy/monacoin.png",
                LightningImagePath = "imlegacy/mona-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("22'") : new KeyPath("1'")
            };
        }
    }
}
