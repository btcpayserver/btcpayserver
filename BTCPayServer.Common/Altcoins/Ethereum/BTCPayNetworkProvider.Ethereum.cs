#if ALTCOINS_RELEASE || DEBUG
using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitEthereum()
        {
            Add(new EthereumBTCPayNetwork()
            {
                CryptoCode = "ETH",
                DisplayName = "Ethereum",
                DefaultRateRules = new[] {"ETH_X = ETH_BTC * BTC_X", "ETH_BTC = kraken(ETH_BTC)"},
                BlockExplorerLink =
                    NetworkType == NetworkType.Mainnet
                        ? "https://etherscan.io/address/{0}"
                        : "https://ropsten.etherscan.io/address/{0}",
                CryptoImagePath = "/imlegacy/eth.png",
                ShowSyncSummary = true,
                CoinType = NetworkType == NetworkType.Mainnet? 60 : 1,
                ChainId = NetworkType == NetworkType.Mainnet ? 1 : 3,
                Divisibility = 18,
                ShowTrailingZeroDecimals = false
            });
        }
        
        public void InitERC20()
        {
            if (NetworkType != NetworkType.Mainnet)
            {
                return;
            }
            Add(new ERC20BTCPayNetwork()
            {
                CryptoCode = "USDT20",
                DisplayName = "Tether USD (ERC20)",
                DefaultRateRules = new[]
                {
                    "USDT20_UST = 1",
                    "USDT20_X = USDT20_BTC * BTC_X",
                    "USDT20_BTC = bitfinex(UST_BTC)",
                },
                BlockExplorerLink =
                    NetworkType == NetworkType.Mainnet
                        ? "https://etherscan.io/address/{0}#tokentxns"
                        : "https://ropsten.etherscan.io/address/{0}#tokentxns",
                CryptoImagePath = "/imlegacy/liquid-tether.svg",
                ShowSyncSummary = false,
                CoinType = NetworkType == NetworkType.Mainnet? 60 : 1,
                ChainId = NetworkType == NetworkType.Mainnet ? 1 : 3,
                SmartContractAddress = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
                Divisibility = 6
            });
        }
    }
}
#endif
