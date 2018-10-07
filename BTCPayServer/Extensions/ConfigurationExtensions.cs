using System;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace BTCPayServer
{
    public static class ConfigurationExtensions
    {
        public static string GetDataDir(this IConfiguration configuration)
        {
            var networkType = DefaultConfiguration.GetNetworkType(configuration);
            return GetDataDir(configuration, networkType);
        }

        public static string GetDataDir(this IConfiguration configuration, NetworkType networkType)
        {
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            return configuration.GetOrDefault<string>("datadir", defaultSettings.DefaultDataDirectory);
        }
        
        public static Uri GetExternalUri(this IConfiguration configuration)
        {
            return configuration.GetOrDefault<Uri>("externalurl", null);
        }
    }
}
