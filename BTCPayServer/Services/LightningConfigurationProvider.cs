using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class LightningConfigurationProvider
    {
        ConcurrentDictionary<ulong, (DateTimeOffset expiration, LightningConfigurations config)> _Map = new ConcurrentDictionary<ulong, (DateTimeOffset expiration, LightningConfigurations config)>();
        public ulong KeepConfig(ulong secret, LightningConfigurations configuration)
        {
            CleanExpired();
            _Map.AddOrReplace(secret, (DateTimeOffset.UtcNow + TimeSpan.FromMinutes(10), configuration));
            return secret;
        }

        public LightningConfigurations GetConfig(ulong secret)
        {
            CleanExpired();
            if (!_Map.TryGetValue(secret, out var value))
                return null;
            return value.config;
        }

        private void CleanExpired()
        {
            foreach(var item in _Map)
            {
                if(item.Value.expiration < DateTimeOffset.UtcNow)
                {
                    _Map.TryRemove(item.Key, out var unused);
                }
            }
        }
    }

    public class LightningConfigurations
    {
        public List<LightningConfiguration> Configurations { get; set; } = new List<LightningConfiguration>();
    }
    public class LightningConfiguration
    {
        public string ChainType { get; set; }
        public string Type { get; set; }
        public string CryptoCode { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool SSL { get; set; }
        public string CertificateThumbprint { get; set; }
        public string Macaroon { get; set; }
    }
}
