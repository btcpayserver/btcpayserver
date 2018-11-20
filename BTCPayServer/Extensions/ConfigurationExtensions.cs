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
            return configuration.GetOrDefault("datadir", defaultSettings.DefaultDataDirectory);
        }

        public static Uri GetExternalUri(this IConfiguration configuration)
        {
            return configuration.GetOrDefault<Uri>("externalurl", null);
        }

        public static bool GetOpenIdEnforceClients(this IConfiguration configuration)
        {
            return configuration.GetValue("openid_enforce_clientId", false);
        }

        public static bool GetOpenIdEnforceGrantTypes(this IConfiguration configuration)
        {
            return configuration.GetValue("openid_enforce_grant_type", false);
        }

        public static bool GetOpenIdEnforceScopes(this IConfiguration configuration)
        {
            return configuration.GetValue("openid_enforce_scope", false);
        } 
        public static bool GetOpenIdEnforceEndpoints(this IConfiguration configuration)
        {
            return configuration.GetValue("openid_enforce_endpoints", false);
        }
    }
}
