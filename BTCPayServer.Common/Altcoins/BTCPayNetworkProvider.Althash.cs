using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitAlthash()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("HTML");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Htmlcoin",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://explorer.htmlcoin.com/api/tx/{0}" : "https://explorer.htmlcoin.com/api/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                DefaultRateRules = new[]
                {
                                "HTML_X = HTML_USD",
                                "HTML_USD = hitbtc(HTML_USD)"
                },
                CryptoImagePath = "imlegacy/althash.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("172'") : new KeyPath("1'")
            });
        }
    }
}
