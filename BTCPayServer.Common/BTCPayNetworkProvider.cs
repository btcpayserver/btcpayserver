using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Contracts.BTCPayServer;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public class BTCPayNetworkProvider
    {
        private readonly string[] _cryptoCodes;
        private readonly IEnumerable<IBTCPayNetworkProvider> _btcPayNetworkProviders;
        private NBXplorerNetworkProvider _NBXplorerNetworkProvider;
        private Dictionary<string, BTCPayNetworkBase> _Networks;

        public NBXplorerNetworkProvider NBXplorerNetworkProvider
        {
            get
            {
                return _NBXplorerNetworkProvider;
            }
        }
        
        public BTCPayNetworkProvider UnfilteredNetworks { get; }

        public NetworkType NetworkType { get; private set; }

        public BTCPayNetworkProvider(IEnumerable<IBTCPayNetworkProvider> btcPayNetworkProviders, NetworkType networkType)
        {
            _btcPayNetworkProviders = btcPayNetworkProviders;
            NetworkType = networkType;
        }
        
        BTCPayNetworkProvider(BTCPayNetworkProvider unfiltered, string[] cryptoCodes)
        {
            _cryptoCodes = cryptoCodes.Select(c => c.ToUpperInvariant()).ToArray();
            UnfilteredNetworks = unfiltered.UnfilteredNetworks ?? unfiltered;
            NetworkType = unfiltered.NetworkType;
            _NBXplorerNetworkProvider = unfiltered.NBXplorerNetworkProvider;
            
            
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
        public BTCPayNetwork BTC => GetNetwork<BTCPayNetwork>("BTC");

        
        public IEnumerable<BTCPayNetworkBase> GetAll(bool refresh = false)
        {
            if (UnfilteredNetworks != null)
            {
                return UnfilteredNetworks.GetAll(refresh).Where(network => _cryptoCodes.Contains(network.CryptoCode, StringComparer.InvariantCultureIgnoreCase));
            }

            if (_Networks == null || refresh)
            {
               _Networks = new Dictionary<string, BTCPayNetworkBase>();
               foreach (var network in _btcPayNetworkProviders.SelectMany(provider => provider.GetNetworks(NetworkType)))
               {
                   _Networks.TryAdd(network.CryptoCode.ToUpperInvariant(), network);
               }
            }

            return _Networks.Values;
        }

        public bool Support(string cryptoCode)
        {
            return GetNetwork(cryptoCode) != null;
        }
        public BTCPayNetworkBase GetNetwork(string cryptoCode)
        {
            return GetNetwork<BTCPayNetworkBase>(cryptoCode.ToUpperInvariant());
        }
        public T GetNetwork<T>(string cryptoCode) where T: BTCPayNetworkBase
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            return GetAll().SingleOrDefault(network => network.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)) as T;
        }
    }
}
