using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BTCPayServer.Common;
using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBXplorer;

namespace BTCPayServer
{
    public class ExplorerClientProvider : IExplorerClientProvider
    {
        readonly BTCPayNetworkProvider _NetworkProviders;

        public BTCPayNetworkProvider NetworkProviders => _NetworkProviders;

        public Logs Logs { get; }

        readonly NBXplorerDashboard _Dashboard;

        public ExplorerClientProvider(
            IHttpClientFactory httpClientFactory,
            BTCPayNetworkProvider networkProviders,
            IOptions<NBXplorerOptions> nbXplorerOptions,
            NBXplorerDashboard dashboard,
            Logs logs)
        {
            Logs = logs;
            _Dashboard = dashboard;
            _NetworkProviders = networkProviders;

            foreach (var setting in nbXplorerOptions.Value.NBXplorerConnectionSettings)
            {
                var cookieFile = setting.CookieFile;
                if (cookieFile.Trim() == "0" || string.IsNullOrEmpty(cookieFile.Trim()))
                    cookieFile = null;
                Logs.Configuration.LogInformation($"{setting.CryptoCode}: Explorer url is {(setting.ExplorerUri.AbsoluteUri)}");
                Logs.Configuration.LogInformation($"{setting.CryptoCode}: Cookie file is {(setting.CookieFile ?? "not set")}");
                if (setting.ExplorerUri != null)
                {
                    _Clients.TryAdd(setting.CryptoCode.ToUpperInvariant(),
                        CreateExplorerClient(httpClientFactory.CreateClient(nameof(ExplorerClientProvider)),
                            _NetworkProviders.GetNetwork<BTCPayNetwork>(setting.CryptoCode), setting.ExplorerUri,
                            setting.CookieFile));
                }
            }
        }

        private ExplorerClient CreateExplorerClient(HttpClient httpClient, BTCPayNetwork n, Uri uri,
            string cookieFile)
        {
            var explorer = n.NBXplorerNetwork.CreateExplorerClient(uri);
            explorer.SetClient(httpClient);
            if (cookieFile == null)
            {
                Logs.Configuration.LogWarning($"{explorer.CryptoCode}: Not using cookie authentication");
                explorer.SetNoAuth();
            }

            if (!explorer.SetCookieAuth(cookieFile))
            {
                Logs.Configuration.LogWarning(
                    $"{explorer.CryptoCode}: Using cookie auth against NBXplorer, but {cookieFile} is not found");
            }

            return explorer;
        }

        readonly Dictionary<string, ExplorerClient> _Clients = new Dictionary<string, ExplorerClient>();

        public ExplorerClient GetExplorerClient(string cryptoCode)
        {
            var network = _NetworkProviders.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null)
                return null;
            _Clients.TryGetValue(network.NBXplorerNetwork.CryptoCode, out ExplorerClient client);
            return client;
        }

        public ExplorerClient GetExplorerClient(BTCPayNetworkBase network)
        {
            ArgumentNullException.ThrowIfNull(network);
            return GetExplorerClient(network.CryptoCode);
        }

        public bool IsAvailable(BTCPayNetworkBase network)
        {
            return IsAvailable(network.CryptoCode);
        }

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _Clients.ContainsKey(cryptoCode) && _Dashboard.IsFullySynched(cryptoCode, out var unused);
        }

        public BTCPayNetwork GetNetwork(string cryptoCode)
        {
            var network = _NetworkProviders.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null)
                return null;
            if (_Clients.ContainsKey(network.NBXplorerNetwork.CryptoCode))
                return network;
            return null;
        }

        public IEnumerable<(BTCPayNetwork, ExplorerClient)> GetAll()
        {
            foreach (var net in _NetworkProviders.GetAll().OfType<BTCPayNetwork>())
            {
                if (_Clients.TryGetValue(net.NBXplorerNetwork.CryptoCode, out ExplorerClient explorer))
                {
                    yield return (net, explorer);
                }
            }
        }
    }
}
