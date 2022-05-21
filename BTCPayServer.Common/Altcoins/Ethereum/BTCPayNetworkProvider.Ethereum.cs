#if ALTCOINS
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
                    NetworkType == ChainName.Mainnet
                        ? "https://etherscan.io/address/{0}"
                        : "https://ropsten.etherscan.io/address/{0}",
                CryptoImagePath = "/imlegacy/eth.png",
                ShowSyncSummary = true,
                CoinType = NetworkType == ChainName.Mainnet? 60 : 1,
                ChainId = NetworkType == ChainName.Mainnet ? 1 : 3,
                Divisibility = 18,
            });
        }
        
        public void InitERC20()
        {
            if (NetworkType != ChainName.Mainnet)
            {
                Add(new ERC20BTCPayNetwork()
                {
                    CryptoCode = "FAU",
                    DisplayName = "Faucet Token",
                    DefaultRateRules = new[]
                    {
                        "FAU_X = FAU_BTC * BTC_X",
                        "FAU_BTC = 0.01",
                    },
                    BlockExplorerLink = "https://ropsten.etherscan.io/address/{0}#tokentxns",
                    ShowSyncSummary = false,
                    CoinType =  1,
                    ChainId =  3,
                    //use https://erc20faucet.com for testnet
                    SmartContractAddress = "0xFab46E002BbF0b4509813474841E0716E6730136",
                    Divisibility = 18,
                    CryptoImagePath = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=",
                });
            }
            else
            {
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
                        NetworkType == ChainName.Mainnet
                            ? "https://etherscan.io/address/{0}#tokentxns"
                            : "https://ropsten.etherscan.io/address/{0}#tokentxns",
                    CryptoImagePath = "/imlegacy/liquid-tether.svg",
                    ShowSyncSummary = false,
                    CoinType = NetworkType == ChainName.Mainnet? 60 : 1,
                    ChainId = NetworkType == ChainName.Mainnet ? 1 : 3,
                    SmartContractAddress = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
                    Divisibility = 6
                });
            }
            
        }
    }
}
#endif
