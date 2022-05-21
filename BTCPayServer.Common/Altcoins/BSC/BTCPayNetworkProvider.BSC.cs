#if ALTCOINS
using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {

        public void InitBNB()
        {
            Add(new BSCBTCPayNetwork()
            {
                CryptoCode = "BNB",
                DisplayName = "BNB",
                DefaultRateRules = new[]
                {
                    "bPROSUS_X = bPROSUS_BTC * BTC_X",
                    "BPROSUS_BTC = bprosus(BPROSUS_BTC)",
                },
                BlockExplorerLink =
                    NetworkType == ChainName.Mainnet
                        ? "https://bscscan.com/{0}"
                        : "https://testnet.bscscan.com/{0}",
                CryptoImagePath = "/imlegacy/bprosus.png",
                ShowSyncSummary = true,
                CoinType = NetworkType == ChainName.Mainnet? 714 : 1,
                ChainId = NetworkType == ChainName.Mainnet ? 56 : 97,
                Divisibility = 18,
            });
        }
        
        public void InitBPROSUS()
        {
            if (NetworkType != ChainName.Mainnet)
            {
                // This is to run BPROSUS in mainnet even though BTCPayserver is not in mainnet
                Add(new BEP20BTCPayNetwork()
                {
                    CryptoCode = "bPROSUS",
                    DisplayName = "PROSUS-BSC",
                    DefaultRateRules = new[]
                    {
                        "bPROSUS_X = bPROSUS_BTC * BTC_X",
                        "BPROSUS_BTC = bprosus(BPROSUS_BTC)",
                    },
                    BlockExplorerLink = "https://bscscan.com/token/0xCDfd3D7817F9402e58a428CF304Cb7493e98336D/?a={0}",
                    CryptoImagePath = "/imlegacy/bprosus.png",
                    ShowSyncSummary = false,
                    CoinType = 714,
                    ChainId = 56,
                    SmartContractAddress = "0xCDfd3D7817F9402e58a428CF304Cb7493e98336D",
                    Divisibility = 12
                });
                // Add(new BEP20BTCPayNetwork()
                // {
                //     CryptoCode = "bPROSUS",
                //     DisplayName = "PROSUS-BSC",
                //     DefaultRateRules = new[]
                //     {
                //         "bPROSUS_X = bPROSUS_BTC * BTC_X",
                //         "BPROSUS_BTC = bprosus(BPROSUS_BTC)",
                //     },
                //     BlockExplorerLink = "https://testnet.bscscan.com/token/0xb0D86E2C31b153FfF675c449226777Cc4ddcd177/?a={0}",
                //     ShowSyncSummary = false,
                //     CoinType =  714,
                //     ChainId =  97,
                //     //use https://erc20faucet.com for testnet
                //     SmartContractAddress = "0xb0D86E2C31b153FfF675c449226777Cc4ddcd177",
                //     Divisibility = 12,
                //     CryptoImagePath = "/imlegacy/bprosus.png",
                // });
            }
            else
            {
                Add(new BEP20BTCPayNetwork()
                {
                    CryptoCode = "bPROSUS",
                    DisplayName = "PROSUS-BSC",
                    DefaultRateRules = new[]
                    {
                        "bPROSUS_X = bPROSUS_BTC * BTC_X",
                        "BPROSUS_BTC = bprosus(BPROSUS_BTC)",
                    },
                    BlockExplorerLink =
                        NetworkType == ChainName.Mainnet
                            ? "https://bscscan.com/token/0xCDfd3D7817F9402e58a428CF304Cb7493e98336D/?a={0}"
                            : "https://testnet.bscscan.com/token/0xCDfd3D7817F9402e58a428CF304Cb7493e98336D/?a={0}",
                    CryptoImagePath = "/imlegacy/bprosus.png",
                    ShowSyncSummary = false,
                    CoinType = NetworkType == ChainName.Mainnet? 714 : 1,
                    ChainId = NetworkType == ChainName.Mainnet ? 56 : 97,
                    SmartContractAddress = "0xCDfd3D7817F9402e58a428CF304Cb7493e98336D",
                    Divisibility = 12
                });
            }
            
        }
    }
}
#endif
