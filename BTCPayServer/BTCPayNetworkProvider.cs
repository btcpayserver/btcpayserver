using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitpayClient;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        Dictionary<string, BTCPayNetwork> _Networks = new Dictionary<string, BTCPayNetwork>();


        private readonly NBXplorerNetworkProvider _NBXplorerNetworkProvider;
        public NBXplorerNetworkProvider NBXplorerNetworkProvider
        {
            get
            {
                return _NBXplorerNetworkProvider;
            }
        }

        public BTCPayNetworkProvider(ChainType chainType)
        {
            _NBXplorerNetworkProvider = new NBXplorerNetworkProvider(chainType);
            InitBitcoin();
            InitLitecoin();
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
