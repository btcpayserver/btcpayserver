using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class LightningConfigurationProvider
    {
        readonly ConcurrentDictionary<ulong, (DateTimeOffset expiration, LightningConfigurations config)> _Map = new ConcurrentDictionary<ulong, (DateTimeOffset expiration, LightningConfigurations config)>();
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
            foreach (var item in _Map)
            {
                if (item.Value.expiration < DateTimeOffset.UtcNow)
                {
                    _Map.TryRemove(item.Key, out var unused);
                }
            }
        }
    }

    public class LightningConfigurations
    {
        public List<object> Configurations { get; set; } = new List<object>();
    }

    public class LNDConfiguration
    {
        public string ChainType { get; set; }
        public string Type { get; set; }
        public string CryptoCode { get; set; }
        public string CertificateThumbprint { get; set; }
        public string Macaroon { get; set; }
        public string AdminMacaroon { get; set; }
        public string ReadonlyMacaroon { get; set; }
        public string InvoiceMacaroon { get; set; }
    }
    public class LightningConfiguration : LNDConfiguration
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public bool SSL { get; set; }
    }
    public class LNDRestConfiguration : LNDConfiguration
    {
        public string Uri { get; set; }
    }
}
