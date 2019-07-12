using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public static class BTCPayNetworkProviderFactory
    {
        public static BTCPayNetworkProvider GetProvider(NetworkType type)
        {
            return new BTCPayNetworkProvider(GetDefaultNetworkProviders(), type);
        }

        public static IEnumerable<IBTCPayNetworkProvider> GetDefaultNetworkProviders()
        {
            return new IBTCPayNetworkProvider[] {new BitcoinBTCPayNetworkProvider(), new ShitcoinBTCPayNetworkProvider()};
        }
    }

    public class BTCPayNetworkProvider
    {
        private readonly IEnumerable<IBTCPayNetworkProvider> _BtcPayNetworkProviders;
        public NetworkType NetworkType;
        private Dictionary<string, BTCPayNetworkBase> _Networks;
        public BTCPayNetworkProvider(IEnumerable<IBTCPayNetworkProvider> btcPayNetworkProviders)
        {
            _BtcPayNetworkProviders = btcPayNetworkProviders;
        }

        public BTCPayNetworkProvider(IEnumerable<IBTCPayNetworkProvider> btcPayNetworkProviders, NetworkType networkType)
        {
            _BtcPayNetworkProviders = btcPayNetworkProviders;
            Init(networkType);

        }
        public void Init(NetworkType networkType)
        {
            NetworkType= networkType;
            _Networks = _BtcPayNetworkProviders.SelectMany(provider => provider.GetNetworks(networkType))
                .ToDictionary(x => x.CryptoCode, x => x);
        }

        [Obsolete("To use only for legacy stuff")]
        public BTCPayNetwork BTC => GetNetwork<BTCPayNetwork>("BTC");

        public IEnumerable<BTCPayNetworkBase> GetAll()
        {
            return _Networks.Values.ToArray();
        }

        public IEnumerable<BTCPayNetworkBase> Filter(string[] cryptoCodes)
        {
            return _Networks.Where(pair => cryptoCodes.Contains(pair.Key)).Select(pair => pair.Value);
        }
        
        public bool Support(string cryptoCode)
        {
            return _Networks.ContainsKey(cryptoCode.ToUpperInvariant());
        }
        public BTCPayNetworkBase GetNetwork(string cryptoCode)
        {
            return GetNetwork<BTCPayNetworkBase>(cryptoCode);
        }
        public T GetNetwork<T>(string cryptoCode) where T: BTCPayNetworkBase
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            if(!_Networks.TryGetValue(cryptoCode.ToUpperInvariant(), out BTCPayNetworkBase network))
            {
                if (cryptoCode == "XBT")
                    return GetNetwork<T>("BTC");
            }
            return network as T;
        }
    }
    
    public partial class ShitcoinBTCPayNetworkProvider: IBTCPayNetworkProvider
    {
        Dictionary<string, BTCPayNetwork> _Networks = new Dictionary<string, BTCPayNetwork>();


        private NBXplorerNetworkProvider _NBXplorerNetworkProvider;
        public NBXplorerNetworkProvider NBXplorerNetworkProvider
        {
            get
            {
                return _NBXplorerNetworkProvider;
            }
        }
        

        public NetworkType NetworkType { get; private set; }
      

        
        private void Add(BTCPayNetwork network)
        {
            _Networks.Add(network.CryptoCode.ToUpperInvariant(), network);
        }

        public IEnumerable<BTCPayNetworkBase> GetNetworks(NetworkType networkType)
        {
            _NBXplorerNetworkProvider = new NBXplorerNetworkProvider(networkType);
            NetworkType = networkType;
            InitLitecoin();
            InitBitcore();
            InitDogecoin();
            InitBitcoinGold();
            InitMonacoin();
            InitDash();
            InitFeathercoin();
            InitGroestlcoin();
            InitViacoin();

            // Assume that electrum mappings are same as BTC if not specified
            foreach (var network in _Networks.Values.OfType<BTCPayNetwork>())
            {
                if(network.ElectrumMapping.Count == 0)
                {
                    network.ElectrumMapping = BitcoinBTCPayNetworkProvider.GetElectrumMapping(networkType);
                    if (!network.NBitcoinNetwork.Consensus.SupportSegwit)
                    {
                        network.ElectrumMapping =
                            network.ElectrumMapping
                                .Where(kv => kv.Value == DerivationType.Legacy)
                                .ToDictionary(k => k.Key, k => k.Value);
                    }
                }
            }

            // Disabled because of https://twitter.com/Cryptopia_NZ/status/1085084168852291586
            //InitPolis();
            //InitBitcoinplus();
            //InitUfo();
            return _Networks.Values;
        }
    }
}
