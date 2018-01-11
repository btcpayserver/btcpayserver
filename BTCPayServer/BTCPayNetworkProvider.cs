using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitpayClient;
using NBXplorer.Configuration;

namespace BTCPayServer
{
    public class BTCPayNetworkProvider
    {
        static BTCPayNetworkProvider()
        {
            NBXplorer.Altcoins.Litecoin.Networks.EnsureRegistered();
        }
        Dictionary<string, BTCPayNetwork> _Networks = new Dictionary<string, BTCPayNetwork>();
        public BTCPayNetworkProvider(ChainType chainType)
        {
            var coinaverage = new CoinAverageRateProvider("BTC");
            var bitpay = new BitpayRateProvider(new Bitpay(new Key(), new Uri("https://bitpay.com/")));
            var btcRate = new FallbackRateProvider(new IRateProvider[] { coinaverage, bitpay });

            var ltcRate = new CoinAverageRateProvider("LTC");
            if (chainType == ChainType.Main)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://www.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.Main,
                    UriScheme = "bitcoin",
                    DefaultRateProvider = btcRate,
                    CryptoImagePath = "imlegacy/bitcoin-symbol.svg"
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "https://live.blockcypher.com/ltc/tx/{0}/",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Mainnet,
                    UriScheme = "litecoin",
                    DefaultRateProvider = ltcRate,
                    CryptoImagePath = "imlegacy/litecoin-symbol.svg"
                });
            }

            if (chainType == ChainType.Test)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://testnet.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.TestNet,
                    UriScheme = "bitcoin",
                    DefaultRateProvider = btcRate,
                    CryptoImagePath = "imlegacy/bitcoin-symbol.svg"
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "http://explorer.litecointools.com/tx/{0}",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Testnet,
                    UriScheme = "litecoin",
                    DefaultRateProvider = ltcRate,
                    CryptoImagePath = "imlegacy/litecoin-symbol.svg"
                });
            }

            if (chainType == ChainType.Regtest)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://testnet.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.RegTest,
                    UriScheme = "bitcoin",
                    DefaultRateProvider = btcRate,
                    CryptoImagePath = "imlegacy/bitcoin-symbol.svg"
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "http://explorer.litecointools.com/tx/{0}",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Regtest,
                    UriScheme = "litecoin",
                    DefaultRateProvider = ltcRate,
                    CryptoImagePath = "imlegacy/litecoin-symbol.svg",
                });
            }

            foreach(var n in _Networks)
            {
                n.Value.NBXplorerNetwork = NetworkInformation.GetNetworkByName(n.Value.NBitcoinNetwork.Name);
                n.Value.DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(chainType);
            }
        }

        [Obsolete("To use only for legacy stuff")]
        public BTCPayNetwork BTC
        {
            get
            {
                return GetNetwork("BTC");
            }
        }

        public void Add(BTCPayNetwork network)
        {
            _Networks.Add(network.CryptoCode, network);
        }

        public IEnumerable<BTCPayNetwork> GetAll()
        {
            return _Networks.Values.ToArray();
        }

        public BTCPayNetwork GetNetwork(string cryptoCode)
        {
            _Networks.TryGetValue(cryptoCode.ToUpperInvariant(), out BTCPayNetwork network);
            return network;
        }
    }
}
