using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer
{
    public class BTCPayNetworkProvider
    {
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
                    UriScheme = "bitcoin"
                });
            }

            if (network == Network.TestNet)
            {
                Add(new BTCPayNetwork()
                {
                    CryptoCode = "BTC",
                    BlockExplorerLink = "https://testnet.smartbit.com.au/tx/{0}",
                    NBitcoinNetwork = Network.TestNet,
                    UriScheme = "bitcoin"
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
            }
        }

        [Obsolete("Should not be needed")]
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

        public BTCPayNetwork GetNetwork(string cryptoCode)
        {
            _Networks.TryGetValue(cryptoCode.ToUpperInvariant(), out BTCPayNetwork network);
            return network;
        }
    }
}
