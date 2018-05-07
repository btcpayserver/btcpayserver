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

        BTCPayNetworkProvider(BTCPayNetworkProvider filtered, string[] cryptoCodes)
        {
            NetworkType = filtered.NetworkType;
            _NBXplorerNetworkProvider = new NBXplorerNetworkProvider(filtered.NetworkType);
            _Networks = new Dictionary<string, BTCPayNetwork>();
            cryptoCodes = cryptoCodes.Select(c => c.ToUpperInvariant()).ToArray();
            foreach (var network in filtered._Networks)
            {
                if(cryptoCodes.Contains(network.Key))
                {
                    _Networks.Add(network.Key, network.Value);
                }
            }
        }

        public NetworkType NetworkType { get; private set; }
        public BTCPayNetworkProvider(NetworkType networkType)
        {
            _NBXplorerNetworkProvider = new NBXplorerNetworkProvider(networkType);
            NetworkType = networkType;
            InitBitcoin();
            InitLitecoin();
            InitDogecoin();
            InitBitcoinGold();
        }

        /// <summary>
        /// Keep only the specified crypto
        /// </summary>
        /// <param name="cryptoCodes">Crypto to support</param>
        /// <returns></returns>
        public BTCPayNetworkProvider Filter(string[] cryptoCodes)
        {
            return new BTCPayNetworkProvider(this, cryptoCodes);
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
            _Networks.Add(network.CryptoCode.ToUpperInvariant(), network);
        }

        public IEnumerable<BTCPayNetwork> GetAll()
        {
            return _Networks.Values.ToArray();
        }

        public bool Support(string cryptoCode)
        {
            return _Networks.ContainsKey(cryptoCode.ToUpperInvariant());
        }

        public BTCPayNetwork GetNetwork(string cryptoCode)
        {
            if(!_Networks.TryGetValue(cryptoCode.ToUpperInvariant(), out BTCPayNetwork network))
            {
                if (cryptoCode == "XBT")
                    return GetNetwork("BTC");
            }
            return network;
        }
    }
}
