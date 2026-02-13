#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public class BTCPayNetworkProvider
    {
        protected readonly Dictionary<string, BTCPayNetworkBase> _Networks = new Dictionary<string, BTCPayNetworkBase>();

        public NBXplorerNetworkProvider NBXplorerNetworkProvider { get; }

        public ChainName NetworkType { get; private set; }
        public BTCPayNetworkProvider(
            IEnumerable<BTCPayNetworkBase> networks,
            NBXplorerNetworkProvider nbxplorerNetworkProvider,
            Logs logs)
        {
            var networksList = networks.ToList();
            NBXplorerNetworkProvider = nbxplorerNetworkProvider;
            NetworkType = nbxplorerNetworkProvider.NetworkType;
            foreach (var network in networksList)
            {
                _Networks.Add(network.CryptoCode.ToUpperInvariant(), network);
            }

            logs.Configuration.LogInformation("Supported chains: {Chains}", string.Join(',', _Networks.Select(n => n.Key).ToArray()));
        }

        public BTCPayNetwork BTC => GetNetwork<BTCPayNetwork>("BTC") ?? throw new InvalidOperationException("BTC network is required");
        public BTCPayNetworkBase? DefaultNetwork => GetNetwork<BTCPayNetwork>("BTC") ?? GetAll().FirstOrDefault();
        /// <summary>
        /// Returns the default network crypto code (BTC) or NONE if no default network is set
        /// </summary>
        public string DefaultCryptoCode => DefaultNetwork?.CryptoCode ?? "NONE";
        public IEnumerable<BTCPayNetworkBase> GetAll()
        {
            return _Networks.Values.ToArray();
        }

        public bool Support(string cryptoCode)
        {
            return _Networks.ContainsKey(cryptoCode.ToUpperInvariant());
        }
        public BTCPayNetworkBase? GetNetwork(string cryptoCode)
        {
            return GetNetwork<BTCPayNetworkBase>(cryptoCode.ToUpperInvariant());
        }
        public T? GetNetwork<T>(string cryptoCode) where T : BTCPayNetworkBase
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
            if (!_Networks.TryGetValue(cryptoCode.ToUpperInvariant(), out var network))
            {
                if (cryptoCode == "XBT")
                    return GetNetwork<T>("BTC");
            }
            return network as T;
        }
        public bool TryGetNetwork<T>(string cryptoCode, [MaybeNullWhen(false)] out T network) where T : BTCPayNetworkBase
        {
            network = GetNetwork<T>(cryptoCode);
            return network != null;
        }
    }
}
