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
        public ChainType ChainType
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
            ChainType = DefaultConfiguration.GetChainType(conf);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainType);
            DataDir = conf.GetOrDefault<string>("datadir", defaultSettings.DefaultDataDirectory);
            Logs.Configuration.LogInformation("Network: " + ChainType.ToString());

            var supportedChains = conf.GetOrDefault<string>("chains", "btc")
                                      .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(t => t.ToUpperInvariant());
            var validChains = new List<string>();
            foreach (var net in new BTCPayNetworkProvider(ChainType).GetAll())
            {
                if (supportedChains.Contains(net.CryptoCode))
                {
                    validChains.Add(net.CryptoCode);
                    var explorer = conf.GetOrDefault<Uri>($"{net.CryptoCode}.explorer.url", net.NBXplorerNetwork.DefaultSettings.DefaultUrl);
                    var cookieFile = conf.GetOrDefault<string>($"{net.CryptoCode}.explorer.cookiefile", net.NBXplorerNetwork.DefaultSettings.DefaultCookieFile);
                    if (cookieFile.Trim() == "0" || string.IsNullOrEmpty(cookieFile.Trim()))
                        cookieFile = null;
                    if (explorer != null)
                    {
                        ExplorerFactories.Add(net.CryptoCode, (n) => CreateExplorerClient(n, explorer, cookieFile));
                    }
                }
            }
            var invalidChains = String.Join(',', supportedChains.Where(s => !validChains.Contains(s)).ToArray());
            if(!string.IsNullOrEmpty(invalidChains))
                throw new ConfigException($"Invalid chains {invalidChains}");

            Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));
            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
            ExternalUrl = conf.GetOrDefault<Uri>("externalurl", null);
        }

        private static ExplorerClient CreateExplorerClient(BTCPayNetwork n, Uri uri, string cookieFile)
        {
            var explorer = new ExplorerClient(n.NBXplorerNetwork, uri);
            if (cookieFile == null || !explorer.SetCookieAuth(cookieFile))
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
