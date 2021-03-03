#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitMatic()
        {
            // this will add the "base token" of the network
            // EG:
            // * on L2 Matic network --> MATIC 
            // read from end variable   
            var ethereumNetwork = "matic";
           
            string networkType = NetworkType == NetworkType.Mainnet? "mainnet" : "testnet";
            var ethereumNetworkData = LoadEthereumNetworkData(networkType, ethereumNetwork);
          
            Add(new MaticBTCPayNetwork()
            {
                CryptoCode = ethereumNetworkData.BaseTokenSymbol,
                DisplayName = "Ethereum",
                DefaultRateRules = new[] {"MATIC_X = ETH_BTC * BTC_X", "MATIC_BTC = kraken(MATIC_BTC)"},
                BlockExplorerLink = ethereumNetworkData.Explorer,
                CryptoImagePath = "/imlegacy/eth.png",
                ShowSyncSummary = true,
                CoinType = ethereumNetworkData.CoinType,
                ChainId = ethereumNetworkData.ChainId,
                Divisibility = ethereumNetworkData.BaseTokenDivisibility,
            });
        }
        
        public void InitMaticERC20()
        {
            var ethereumNetwork = "matic";
            string networkType = NetworkType == NetworkType.Mainnet? "mainnet" : "testnet";
            var ethereumNetworkData = LoadEthereumNetworkData(networkType, ethereumNetwork);
            string explorer = ethereumNetworkData.Explorer;
            int chainId = ethereumNetworkData.ChainId;
            int coinType = ethereumNetworkData.CoinType;
        
            var ERC20Tokens = LoadERC20Config(ethereumNetwork + "." + networkType).ToDictionary(k => k.CryptoCode);
            foreach(KeyValuePair<string, BTCPayServer.ERC20Data> entry in ERC20Tokens)
            {
                var token = entry.Value;
                Add(new ERC20MaticBTCPayNetwork()
                {
                    CryptoCode = token.CryptoCode,
                    DisplayName = token.DisplayName,
                    DefaultRateRules = new[]
                    {
                        "USDT20_UST = 1",
                        "USDT20_X = USDT20_BTC * BTC_X",
                        "USDT20_BTC = bitfinex(UST_BTC)",
                    },
                    BlockExplorerLink = explorer,
                    CryptoImagePath = token.CryptoImagePath,
                    ShowSyncSummary = false,
                    CoinType = coinType,
                    ChainId = chainId,
                    SmartContractAddress = token.SmartContractAddress,
                    Divisibility = token.Divisibility
                });
            }

        }

    }

    
}
#endif
