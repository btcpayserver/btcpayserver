using BTCPayServer.Logging;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using StandardConfiguration;
using Microsoft.Extensions.Configuration;
using NBXplorer;

namespace BTCPayServer.Configuration
{
    public class BTCPayServerOptions
    {
        public Network Network
        {
            get; set;
        }
        public string ConfigurationFile
        {
            get;
            private set;
        }
        public string DataDir
        {
            get;
            private set;
        }
        public List<IPEndPoint> Listen
        {
            get;
            set;
        }

        public void LoadArgs(IConfiguration conf)
        {
            var networkInfo = DefaultConfiguration.GetNetwork(conf);
            Network = networkInfo?.Network;
            if (Network == null)
                throw new ConfigException("Invalid network");

            DataDir = conf.GetOrDefault<string>("datadir", networkInfo.DefaultDataDirectory);
            Logs.Configuration.LogInformation("Network: " + Network);


            bool btcHandled = false;
            foreach (var net in new BTCPayNetworkProvider(Network).GetAll())
            {
                var nbxplorer = NBXplorer.Configuration.NetworkInformation.GetNetworkByName(net.NBitcoinNetwork.Name);
                var explorer = conf.GetOrDefault<Uri>($"{net.CryptoCode}.explorer.url", null);
                var cookieFile = conf.GetOrDefault<string>($"{net.CryptoCode}.explorer.cookiefile", nbxplorer.GetDefaultCookieFile());
                if (explorer != null)
                {
#pragma warning disable CS0618
                    if (net.IsBTC)
                        btcHandled = true;
#pragma warning restore CS0618
                    ExplorerFactories.Add(net.CryptoCode, (n) => CreateExplorerClient(n, explorer, cookieFile));
                }
            }

            // Handle legacy explorer.url and explorer.cookiefile
            if (!btcHandled)
            {
                var nbxplorer = NBXplorer.Configuration.NetworkInformation.GetNetworkByName(Network.Name); // Will get BTC info
                var explorer = conf.GetOrDefault<Uri>($"explorer.url", new Uri(nbxplorer.GetDefaultExplorerUrl(), UriKind.Absolute));
                var cookieFile = conf.GetOrDefault<string>($"explorer.cookiefile", nbxplorer.GetDefaultCookieFile());
                ExplorerFactories.Add("BTC", (n) => CreateExplorerClient(n, explorer, cookieFile));
            }
            //////

            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
            ExternalUrl = conf.GetOrDefault<Uri>("externalurl", null);
        }

        private static ExplorerClient CreateExplorerClient(BTCPayNetwork n, Uri uri, string cookieFile)
        {
            var explorer = new ExplorerClient(n.NBitcoinNetwork, uri);
            if (!explorer.SetCookieAuth(cookieFile))
                explorer.SetNoAuth();
            return explorer;
        }

        public Dictionary<string, Func<BTCPayNetwork, ExplorerClient>> ExplorerFactories = new Dictionary<string, Func<BTCPayNetwork, ExplorerClient>>();
        public string PostgresConnectionString
        {
            get;
            set;
        }
        public Uri ExternalUrl
        {
            get;
            set;
        }
    }
}
