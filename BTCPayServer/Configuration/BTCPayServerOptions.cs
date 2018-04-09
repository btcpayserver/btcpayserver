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
using BTCPayServer.Payments.Lightning;

namespace BTCPayServer.Configuration
{
    public class NBXplorerConnectionSetting
    {
        public string CryptoCode { get; internal set; }
        public Uri ExplorerUri { get; internal set; }
        public string CookieFile { get; internal set; }
    }

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

        public List<NBXplorerConnectionSetting> NBXplorerConnectionSettings
        {
            get;
            set;
        } = new List<NBXplorerConnectionSetting>();

        public void LoadArgs(IConfiguration conf)
        {
            ChainType = DefaultConfiguration.GetChainType(conf);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainType);
            DataDir = conf.GetOrDefault<string>("datadir", defaultSettings.DefaultDataDirectory);
            Logs.Configuration.LogInformation("Network: " + ChainType.ToString());

            var supportedChains = conf.GetOrDefault<string>("chains", "btc")
                                      .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(t => t.ToUpperInvariant());
            NetworkProvider = new BTCPayNetworkProvider(ChainType).Filter(supportedChains.ToArray());
            foreach (var chain in supportedChains)
            {
                if (NetworkProvider.GetNetwork(chain) == null)
                    throw new ConfigException($"Invalid chains \"{chain}\"");
            }

            var validChains = new List<string>();
            foreach (var net in NetworkProvider.GetAll())
            {
                NBXplorerConnectionSetting setting = new NBXplorerConnectionSetting();
                setting.CryptoCode = net.CryptoCode;
                setting.ExplorerUri = conf.GetOrDefault<Uri>($"{net.CryptoCode}.explorer.url", net.NBXplorerNetwork.DefaultSettings.DefaultUrl);
                setting.CookieFile = conf.GetOrDefault<string>($"{net.CryptoCode}.explorer.cookiefile", net.NBXplorerNetwork.DefaultSettings.DefaultCookieFile);
                NBXplorerConnectionSettings.Add(setting);
                var lightning = conf.GetOrDefault<string>($"{net.CryptoCode}.lightning", string.Empty);
                if(lightning.Length != 0)
                {
                    if(!LightningConnectionString.TryParse(lightning, out var connectionString, out var error))
                    {
                        throw new ConfigException($"Invalid setting {net.CryptoCode}.lightning, you need to pass either " +
                            $"the absolute path to the unix socket of a running CLightning instance (eg. /root/.lightning/lightning-rpc), " +
                            $"or the url to a charge server with crendetials (eg. https://apitoken@API_TOKEN_SECRET:charge.example.com/)");
                    }
                    InternalLightningByCryptoCode.Add(net.CryptoCode, connectionString);
                }
            }

            Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));

            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
            BundleJsCss = conf.GetOrDefault<bool>("bundlejscss", true);
            ExternalUrl = conf.GetOrDefault<Uri>("externalurl", null);

            RootPath = conf.GetOrDefault<string>("rootpath", "/");
            if (!RootPath.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
                RootPath = "/" + RootPath;
            var old = conf.GetOrDefault<Uri>("internallightningnode", null);
            if(old != null)
                throw new ConfigException($"internallightningnode should not be used anymore, use btclightning instead");
        }
        public string RootPath { get; set; }
        public Dictionary<string, LightningConnectionString> InternalLightningByCryptoCode { get; set; } = new Dictionary<string, LightningConnectionString>();

        public BTCPayNetworkProvider NetworkProvider { get; set; }
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
        public bool BundleJsCss
        {
            get;
            set;
        }
    }
}
