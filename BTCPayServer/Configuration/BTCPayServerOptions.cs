using System;
using System.IO;
using System.Net;
using BTCPayServer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Serilog.Events;

namespace BTCPayServer.Configuration
{
    public class BTCPayServerOptions
    {
        public ChainName NetworkType
        {
            get; set;
        }
        public string ConfigurationFile
        {
            get;
            private set;
        }

        public string LogFile
        {
            get;
            private set;
        }
        public EndPoint SocksEndpoint { get; set; }


        public bool DisableRegistration
        {
            get;
            private set;
        }

        public static string GetDebugLog(IConfiguration configuration)
        {
            var logfile = configuration.GetValue<string>("debuglog", null);
            if (!string.IsNullOrEmpty(logfile))
            {
                if (!Path.IsPathRooted(logfile))
                {
                    logfile = Path.Combine(new DataDirectories().Configure(configuration).DataDir, logfile);
                }
            }
            return logfile;
        }
        public static LogEventLevel GetDebugLogLevel(IConfiguration configuration)
        {
            var raw = configuration.GetValue("debugloglevel", nameof(LogEventLevel.Debug));
            return (LogEventLevel)Enum.Parse(typeof(LogEventLevel), raw, true);
        }

        public void LoadArgs(IConfiguration conf, Logs Logs)
        {
            NetworkType = DefaultConfiguration.GetNetworkType(conf);

            Logs.Configuration.LogInformation("Network: " + NetworkType);

            if (conf.GetOrDefault<string>("POSTGRES", null) == null)
            {
                var allowDeprecated = conf.GetOrDefault<bool>("DEPRECATED", false);
                if (allowDeprecated)
                {
                    if (conf.GetOrDefault<string>("SQLITEFILE", null) != null)
                        Logs.Configuration.LogWarning("SQLITE backend support is out of support. Please migrate to Postgres by following the following instructions https://github.com/btcpayserver/btcpayserver/blob/master/docs/db-migration.md");
                    if (conf.GetOrDefault<string>("MYSQL", null) != null)
                        Logs.Configuration.LogWarning("MYSQL backend support is out of support. Please migrate to Postgres by following the following instructions (https://github.com/btcpayserver/btcpayserver/blob/master/docs/db-migration.md)");
                }
                else
                {
                    if (conf.GetOrDefault<string>("SQLITEFILE", null) != null)
                        throw new ConfigException("SQLITE backend support is out of support. Please migrate to Postgres by following the following instructions (https://github.com/btcpayserver/btcpayserver/blob/master/docs/db-migration.md). If you don't want to update, you can try to start this instance by using the command line argument --deprecated");
                    if (conf.GetOrDefault<string>("MYSQL", null) != null)
                        throw new ConfigException("MYSQL backend support is out of support. Please migrate to Postgres by following the following instructions (https://github.com/btcpayserver/btcpayserver/blob/master/docs/db-migration.md). If you don't want to update, you can try to start this instance by using the command line argument --deprecated");
                }
            }
            DockerDeployment = conf.GetOrDefault<bool>("dockerdeployment", true);
            TorrcFile = conf.GetOrDefault<string>("torrcfile", null);
            TorServices = conf.GetOrDefault<string>("torservices", null)
                ?.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (!string.IsNullOrEmpty(TorrcFile) && TorServices != null)
                throw new ConfigException($"torrcfile or torservices should be provided, but not both");

            var socksEndpointString = conf.GetOrDefault<string>("socksendpoint", null);
            if (!string.IsNullOrEmpty(socksEndpointString))
            {
                if (!Utils.TryParseEndpoint(socksEndpointString, 9050, out var endpoint))
                    throw new ConfigException("Invalid value for socksendpoint");
                SocksEndpoint = endpoint;
            }

            UpdateUrl = conf.GetOrDefault<Uri>("updateurl", null);

            RootPath = conf.GetOrDefault<string>("rootpath", "/");
            if (!RootPath.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
                RootPath = "/" + RootPath;
            var old = conf.GetOrDefault<Uri>("internallightningnode", null);
            if (old != null)
                throw new ConfigException($"internallightningnode is deprecated and should not be used anymore, use btclightning instead");

            LogFile = GetDebugLog(conf);
            if (!string.IsNullOrEmpty(LogFile))
            {
                Logs.Configuration.LogInformation("LogFile: " + LogFile);
                Logs.Configuration.LogInformation("Log Level: " + GetDebugLogLevel(conf));
            }

            DisableRegistration = conf.GetOrDefault<bool>("disable-registration", true);
            CheatMode = conf.GetOrDefault("cheatmode", false);
            if (CheatMode && this.NetworkType == ChainName.Mainnet)
                throw new ConfigException($"cheatmode can't be used on mainnet");
        }

        public bool CheatMode { get; set; }

        public string RootPath { get; set; }
        public bool DockerDeployment { get; set; }
        public string TorrcFile { get; set; }
        public string[] TorServices { get; set; }
        public Uri UpdateUrl { get; set; }
    }
}
