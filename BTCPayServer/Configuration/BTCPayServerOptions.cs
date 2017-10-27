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

namespace BTCPayServer.Configuration
{
    public class BTCPayServerOptions
    {
        public Network Network
        {
            get; set;
        }
        public Uri Explorer
        {
            get; set;
        }

        public string CookieFile
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

            Explorer = conf.GetOrDefault<Uri>("explorer.url", networkInfo.DefaultExplorerUrl);
            CookieFile = conf.GetOrDefault<string>("explorer.cookiefile", networkInfo.DefaultExplorerCookieFile);
            RequireHttps = conf.GetOrDefault<bool>("requirehttps", false);
            PostgresConnectionString = conf.GetOrDefault<string>("postgres", null);
        }

        public bool RequireHttps
        {
            get; set;
        }
        public string PostgresConnectionString
        {
            get;
            set;
        }
    }
}
