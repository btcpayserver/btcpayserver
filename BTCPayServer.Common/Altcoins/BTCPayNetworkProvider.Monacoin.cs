using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitMonacoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("MONA");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Monacoin",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://mona.insight.monaco-ex.org/insight/tx/{0}" : "https://testnet-mona.insight.monaco-ex.org/insight/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                DefaultRateRules = new[]
                {
                                "MONA_X = MONA_BTC * BTC_X",
                                "MONA_BTC = bittrex(MONA_BTC)"
                },
                CryptoImagePath = "imlegacy/monacoin.png",
                LightningImagePath = "imlegacy/mona-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("22'") : new KeyPath("1'")
            });
        }
    }
}
