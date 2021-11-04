using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        protected readonly Dictionary<string, BTCPayNetworkBase> _Networks = new Dictionary<string, BTCPayNetworkBase>();

        private readonly NBXplorerNetworkProvider _NBXplorerNetworkProvider;
        public NBXplorerNetworkProvider NBXplorerNetworkProvider
        {
            get
            {
                return _NBXplorerNetworkProvider;
            }
        }

        BTCPayNetworkProvider(BTCPayNetworkProvider unfiltered, string[] cryptoCodes)
        {
            NetworkType = unfiltered.NetworkType;
            _NBXplorerNetworkProvider = new NBXplorerNetworkProvider(unfiltered.NetworkType);
            _Networks = new Dictionary<string, BTCPayNetworkBase>();
            cryptoCodes = cryptoCodes.Select(c => c.ToUpperInvariant()).ToArray();
            foreach (var network in unfiltered._Networks)
            {
                if (cryptoCodes.Contains(network.Key))
                {
                    _Networks.Add(network.Key, network.Value);
                }
            }
        }


        public ChainName NetworkType { get; private set; }
        public BTCPayNetworkProvider(ChainName networkType)
        {
            _NBXplorerNetworkProvider = new NBXplorerNetworkProvider(networkType);
            NetworkType = networkType;
            InitBitcoin();
#if ALTCOINS
            InitLiquid();
            InitLiquidAssets();
            InitLitecoin();
            InitBitcore();
            InitDogecoin();
            InitBGold();
            InitMonacoin();
            InitDash();
            InitFeathercoin();
            InitAlthash();
            InitGroestlcoin();
            InitViacoin();
            InitMonero();
            InitZcash();
            InitPolis();
            InitChaincoin();
            // InitArgoneum();//their rate source is down 9/15/20.
            InitMonetaryUnit();

            // Assume that electrum mappings are same as BTC if not specified
            foreach (var network in _Networks.Values.OfType<BTCPayNetwork>())
            {
                if (network.ElectrumMapping.Count == 0)
                {
                    network.ElectrumMapping = GetNetwork<BTCPayNetwork>("BTC").ElectrumMapping;
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
            //InitBPlus();
            //InitUfo();
#endif
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

        public void Add(BTCPayNetwork network)
        {
            if (network.NBitcoinNetwork == null)
                return;
            Add(network as BTCPayNetworkBase);
        }
        public void Add(BTCPayNetworkBase network)
        {
            _Networks.Add(network.CryptoCode.ToUpperInvariant(), network);
        }

        public IEnumerable<BTCPayNetworkBase> GetAll()
        {
            return _Networks.Values.ToArray();
        }

        public bool Support(string cryptoCode)
        {
            return _Networks.ContainsKey(cryptoCode.ToUpperInvariant());
        }
        public BTCPayNetworkBase GetNetwork(string cryptoCode)
        {
            return GetNetwork<BTCPayNetworkBase>(cryptoCode.ToUpperInvariant());
        }
        public T GetNetwork<T>(string cryptoCode) where T : BTCPayNetworkBase
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
            if (!_Networks.TryGetValue(cryptoCode.ToUpperInvariant(), out BTCPayNetworkBase network))
            {
                if (cryptoCode == "XBT")
                    return GetNetwork<T>("BTC");
            }
            return network as T;
        }
        public bool TryGetNetwork<T>(string cryptoCode, out T network) where T : BTCPayNetworkBase
        {
            network = GetNetwork<T>(cryptoCode);
            return network != null;
        }
    }
}
