using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
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

            foreach (var setting in options.NBXplorerConnectionSettings)
            {
                var cookieFile = setting.CookieFile;
                if (cookieFile.Trim() == "0" || string.IsNullOrEmpty(cookieFile.Trim()))
                    cookieFile = null;
                Logs.Configuration.LogInformation($"{setting.CryptoCode}: Explorer url is {(setting.ExplorerUri.AbsoluteUri ?? "not set")}");
                Logs.Configuration.LogInformation($"{setting.CryptoCode}: Cookie file is {(setting.CookieFile ?? "not set")}");
                if (setting.ExplorerUri != null)
                {
                    _Clients.TryAdd(setting.CryptoCode, CreateExplorerClient(_NetworkProviders.GetNetwork(setting.CryptoCode), setting.ExplorerUri, setting.CookieFile));
                }
            }
        }

        private static ExplorerClient CreateExplorerClient(BTCPayNetwork n, Uri uri, string cookieFile)
        {
            var explorer = new ExplorerClient(n.NBXplorerNetwork, uri);
            if (cookieFile == null || !explorer.SetCookieAuth(cookieFile))
            {
                Logs.Configuration.LogWarning($"{n.CryptoCode}: Not using cookie authentication");
                explorer.SetNoAuth();
            }
            return explorer;
        }

        Dictionary<string, ExplorerClient> _Clients = new Dictionary<string, ExplorerClient>();

        public ExplorerClient GetExplorerClient(string cryptoCode)
        {
            var network = _NetworkProviders.GetNetwork(cryptoCode);
            if (network == null)
                return null;
            _Clients.TryGetValue(network.CryptoCode, out ExplorerClient client);
            return client;
        }

        public ExplorerClient GetExplorerClient(BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return GetExplorerClient(network.CryptoCode);
        }

        public BTCPayNetwork GetNetwork(string cryptoCode)
        {
            var network = _NetworkProviders.GetNetwork(cryptoCode);
            if (network == null)
                return null;
            if (_Clients.ContainsKey(network.CryptoCode))
                return network;
            return null;
        }

        public IEnumerable<(BTCPayNetwork, ExplorerClient)> GetAll()
        {
            foreach (var net in _NetworkProviders.GetAll())
            {
                if (_Clients.TryGetValue(net.CryptoCode, out ExplorerClient explorer))
                {
                    yield return (net, explorer);
                }
            }
        }
    }
}
