using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitFeathercoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("FTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Feathercoin",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://explorer.feathercoin.com/tx/{0}" : "https://explorer.feathercoin.com/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                DefaultRateRules = new[]
                {
                                "FTC_X = FTC_BTC * BTC_X",
                                "FTC_BTC = bittrex(FTC_BTC)"
                },
                CryptoImagePath = "imlegacy/feathercoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("8'") : new KeyPath("1'")
            });
        }
    }
}
