using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

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
            if (network == Network.Main)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://www.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.Main,
                    UriScheme = "bitcoin",
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "https://live.blockcypher.com/ltc/tx/{0}/",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Mainnet,
                    UriScheme = "litecoin",
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
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "http://explorer.litecointools.com/tx/{0}",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Testnet,
                    UriScheme = "litecoin",
                });
            }

            if (network == Network.RegTest)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://testnet.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.RegTest,
                    UriScheme = "bitcoin"
                });
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "LTC",
                    BlockExplorerLink = "http://explorer.litecointools.com/tx/{0}",
                    NBitcoinNetwork = NBXplorer.Altcoins.Litecoin.Networks.Regtest,
                    UriScheme = "litecoin",
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
