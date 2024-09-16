using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using StandardConfiguration;

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

        public ChainName NetworkType { get; private set; }
        public BTCPayNetworkProvider(
            IEnumerable<BTCPayNetworkBase> networks,
            SelectedChains selectedChains,
            NBXplorerNetworkProvider nbxplorerNetworkProvider,
            Logs logs)
        {
            var networksList = networks.ToList();
            _NBXplorerNetworkProvider = nbxplorerNetworkProvider;
            NetworkType = nbxplorerNetworkProvider.NetworkType;
            foreach (var network in networksList)
            {
                _Networks.Add(network.CryptoCode.ToUpperInvariant(), network);
            }

            foreach (var chain in selectedChains.ExplicitlySelected)
            {
                if (GetNetwork<BTCPayNetworkBase>(chain) == null)
                    throw new ConfigException($"Invalid chains \"{chain}\"");
            }

            logs.Configuration.LogInformation("Supported chains: {Chains}", string.Join(',', _Networks.Select(n => n.Key).ToArray()));
        }

        public BTCPayNetwork BTC => GetNetwork<BTCPayNetwork>("BTC");
        public BTCPayNetworkBase DefaultNetwork => BTC ?? GetAll().First();
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
