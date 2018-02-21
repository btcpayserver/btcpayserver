﻿using BTCPayServer.Logging;
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
            var validChains = new List<string>();
            foreach (var net in new BTCPayNetworkProvider(ChainType).GetAll())
            {
                if (supportedChains.Contains(net.CryptoCode))
                {
                    validChains.Add(net.CryptoCode);
                    NBXplorerConnectionSetting setting = new NBXplorerConnectionSetting();
                    setting.CryptoCode = net.CryptoCode;
                    setting.ExplorerUri = conf.GetOrDefault<Uri>($"{net.CryptoCode}.explorer.url", net.NBXplorerNetwork.DefaultSettings.DefaultUrl);
                    setting.CookieFile = conf.GetOrDefault<string>($"{net.CryptoCode}.explorer.cookiefile", net.NBXplorerNetwork.DefaultSettings.DefaultCookieFile);
                    NBXplorerConnectionSettings.Add(setting);
                }
            }
            var invalidChains = String.Join(',', supportedChains.Where(s => !validChains.Contains(s)).ToArray());
            if(!string.IsNullOrEmpty(invalidChains))
                throw new ConfigException($"Invalid chains {invalidChains}");

            Logs.Configuration.LogInformation("Supported chains: " + String.Join(',', supportedChains.ToArray()));
            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
            BundleJsCss = conf.GetOrDefault<bool>("bundlejscss", true);
            ExternalUrl = conf.GetOrDefault<Uri>("externalurl", null);
        }

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
