using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using NBXplorer;

namespace BTCPayServer
{
    public class ExplorerClientProvider
    {
        BTCPayNetworkProvider _NetworkProviders;
        BTCPayServerOptions _Options;

        public BTCPayNetworkProvider NetworkProviders => _NetworkProviders;

        public ExplorerClientProvider(BTCPayNetworkProvider networkProviders, BTCPayServerOptions options)
        {
            _NetworkProviders = networkProviders;
            _Options = options;
        }

        public ExplorerClient GetExplorerClient(string cryptoCode)
        {
            var network = _NetworkProviders.GetNetwork(cryptoCode);
            if (network == null)
                return null;
            if (_Options.ExplorerFactories.TryGetValue(network.CryptoCode, out Func<BTCPayNetwork, ExplorerClient> factory))
            {
                return factory(network);
            }
            return null;
        }

        public ExplorerClient GetExplorerClient(BTCPayNetwork network)
        {
            return GetExplorerClient(network.CryptoCode);
        }

        public BTCPayNetwork GetNetwork(string cryptoCode)
        {
            var network = _NetworkProviders.GetNetwork(cryptoCode);
            if (network == null)
                return null;
            if (_Options.ExplorerFactories.ContainsKey(network.CryptoCode))
                return network;
            return null;
        }

        public IEnumerable<(BTCPayNetwork, ExplorerClient)> GetAll()
        {
            foreach(var net in _NetworkProviders.GetAll())
            {
                if(_Options.ExplorerFactories.TryGetValue(net.CryptoCode, out Func<BTCPayNetwork, ExplorerClient> factory))
                {
                    yield return (net, factory(net));
                }
            }
        }
    }
}
