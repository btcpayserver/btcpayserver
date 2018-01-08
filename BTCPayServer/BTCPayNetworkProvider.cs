using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitpayClient;

namespace BTCPayServer
{
    public class BTCPayNetworkProvider
    {
        static BTCPayNetworkProvider()
        {
            NBXplorer.Altcoins.Litecoin.Networks.EnsureRegistered();
        }
        Dictionary<string, BTCPayNetwork> _Networks = new Dictionary<string, BTCPayNetwork>();
        public BTCPayNetworkProvider(Network network)
        {
            var coinaverage = new CoinAverageRateProvider("BTC");
            var bitpay = new BitpayRateProvider(new Bitpay(new Key(), new Uri("https://bitpay.com/")));
            var btcRate = new FallbackRateProvider(new IRateProvider[] { coinaverage, bitpay });

            var ltcRate = new CoinAverageRateProvider("LTC");
            if (network == Network.Main)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://www.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.Main,
                    UriScheme = "bitcoin",
                    DefaultRateProvider = btcRate
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "https://live.blockcypher.com/ltc/tx/{0}/",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Mainnet,
                    UriScheme = "litecoin",
                    DefaultRateProvider = ltcRate
                });
            }

            if (network == Network.TestNet)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://testnet.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.TestNet,
                    UriScheme = "bitcoin",
                    DefaultRateProvider = btcRate
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "http://explorer.litecointools.com/tx/{0}",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Testnet,
                    UriScheme = "litecoin",
                    DefaultRateProvider = ltcRate
                });
            }

            if (network == Network.RegTest)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://testnet.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.RegTest,
                    UriScheme = "bitcoin",
                    DefaultRateProvider = btcRate
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "http://explorer.litecointools.com/tx/{0}",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Regtest,
                    UriScheme = "litecoin",
                    DefaultRateProvider = ltcRate
                });
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
