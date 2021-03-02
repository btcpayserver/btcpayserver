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
                CoinType = NetworkType == NetworkType.Mainnet ? 60 : 1,
                ChainId = NetworkType == NetworkType.Mainnet ? 1 : 3,
                Divisibility = 18,
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
/*
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
                */
        }

        static ERC20Data[] LoadERC20Config(string networkName)
        {
            //var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BTCPayServer.Common.Ethereum.erc20_" + networkName + ".json");
           
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
        public string Explorer { get; set; }
    }

    public class EthereumNetworkData
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public int ChainId {get; set; }
        public int CoinType {get; set; }
        public string Explorer { get; set; }
        
    }
}
#endif
