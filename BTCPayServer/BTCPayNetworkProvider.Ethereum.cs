using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitEthereum()
        {
            Add(new BTCPayNetwork()
            {
                CryptoCode = "ETH",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://etherscan.io/tx/{0}/" : "https://ropsten.etherscan.io/tx/{0}",
                UriScheme = "ethereum",
                CryptoImagePath = "imlegacy/ethereum.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("60'") : new KeyPath("1'")
            });
        }
    }
}
