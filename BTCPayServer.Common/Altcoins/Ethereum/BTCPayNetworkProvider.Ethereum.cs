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
        public void InitEthereum()
        {
            // this will add the "base token" of the network
            // EG:
            // * on main Ethereum network --> ETH
            // * on L2 Matic network --> MATIC 
              
            var ethereumNetwork = "matic"; // TODO remove hardcode and read from a config or env
            string networkType = NetworkType == NetworkType.Mainnet? "mainnet" : "testnet";
            var ethereumNetworkData = LoadEthereumNetworkData(networkType, ethereumNetwork);
          
            Add(new EthereumBTCPayNetwork()
            {
                CryptoCode = ethereumNetworkData.BaseTokenSymbol,
                DisplayName = "Ethereum",
                DefaultRateRules = new[] {"ETH_X = ETH_BTC * BTC_X", "ETH_BTC = kraken(ETH_BTC)"},
                BlockExplorerLink = ethereumNetworkData.Explorer,
                CryptoImagePath = "/imlegacy/eth.png",
                ShowSyncSummary = true,
                CoinType = ethereumNetworkData.CoinType,
                ChainId = ethereumNetworkData.ChainId,
                Divisibility = ethereumNetworkData.BaseTokenDivisibility,
            });
        }
        
        public void InitERC20()
        {
            var ethereumNetwork = "matic"; // TODO remove hardcode and read from a config or env
            string networkType = NetworkType == NetworkType.Mainnet? "mainnet" : "testnet";
            var ethereumNetworkData = LoadEthereumNetworkData(networkType, ethereumNetwork);
            string explorer = ethereumNetworkData.Explorer;
            int chainId = ethereumNetworkData.ChainId;
            int coinType = ethereumNetworkData.CoinType;
        
            var ERC20Tokens = LoadERC20Config(ethereumNetwork + "." + networkType).ToDictionary(k => k.CryptoCode);
            foreach(KeyValuePair<string, BTCPayServer.ERC20Data> entry in ERC20Tokens)
            {
                var token = entry.Value;
                Add(new ERC20BTCPayNetwork()
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

        static ERC20Data[] LoadERC20Config(string networkName)
        {
            var content = ReadResource("Erc20" + "." + networkName + ".json");
            var tokens = JsonConvert.DeserializeObject<ERC20Data[]>(content);
            return tokens;
        }

        static EthereumNetworkData LoadEthereumNetworkData(string networkType, string ethereumNetwork)
        {
            string filename = "NetworkInfo" + "." + ethereumNetwork + "." + networkType + ".json";
            
            var content = ReadResource(filename);
            var networkInfo = JsonConvert.DeserializeObject<EthereumNetworkData>(content);
            return networkInfo;
        }

        static string ReadResource(string name)
        {
            // Determine path
            var assembly = Assembly.GetExecutingAssembly();
            string resourcePath = name;
            // Format: "{Namespace}.{Folder}.{filename}.{Extension}"
          
            resourcePath = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith(name));
            

            using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }

    public class ERC20Data
    {
        public string CryptoCode { get; set; }
        public string DisplayName {get; set; }
        public string CryptoImagePath { get; set; }
        public string SmartContractAddress { get; set; }
        public int Divisibility { get; set; }
    }

    public class EthereumNetworkData
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string BaseTokenSymbol { get; set; }
        public int BaseTokenDivisibility { get; set; }
        public int ChainId {get; set; }
        public int CoinType {get; set; }
        public string Explorer { get; set; }
        
    }
}
#endif
