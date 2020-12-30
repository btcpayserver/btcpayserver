using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Configuration
{
    public class LightningNetworkOptions
    {
        public Dictionary<string, LightningConnectionString> InternalLightningByCryptoCode { get; set; } =
            new Dictionary<string, LightningConnectionString>();

        public void Configure(IConfiguration conf, BTCPayNetworkProvider networkProvider)
        {
            foreach (var net in networkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                var lightning = conf.GetOrDefault<string>($"{net.CryptoCode}.lightning", string.Empty);
                if (lightning.Length != 0)
                {
                    if (!LightningConnectionString.TryParse(lightning, true, out var connectionString,
                        out var error))
                    {
                        Logs.Configuration.LogWarning($"Invalid setting {net.CryptoCode}.lightning, " +
                                                      Environment.NewLine +
                                                      $"If you have a c-lightning server use: 'type=clightning;server=/root/.lightning/lightning-rpc', " +
                                                      Environment.NewLine +
                                                      $"If you have a lightning charge server: 'type=charge;server=https://charge.example.com;api-token=yourapitoken'" +
                                                      Environment.NewLine +
                                                      $"If you have a lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" +
                                                      Environment.NewLine +
                                                      $"              lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" +
                                                      Environment.NewLine +
                                                      $"If you have an eclair server: 'type=eclair;server=http://eclair.com:4570;password=eclairpassword;bitcoin-host=bitcoind:37393;bitcoin-auth=bitcoinrpcuser:bitcoinrpcpassword" +
                                                      Environment.NewLine +
                                                      $"               eclair server: 'type=eclair;server=http://eclair.com:4570;password=eclairpassword;bitcoin-host=bitcoind:37393" +
                                                      Environment.NewLine +
                                                      $"Error: {error}" + Environment.NewLine +
                                                      "This service will not be exposed through BTCPay Server");
                    }
                    else
                    {
                        if (connectionString.IsLegacy)
                        {
                            Logs.Configuration.LogWarning(
                                $"Setting {net.CryptoCode}.lightning is a deprecated format, it will work now, but please replace it for future versions with '{connectionString.ToString()}'");
                        }

                        InternalLightningByCryptoCode.Add(net.CryptoCode, connectionString);
                    }
                }
            }
        }
    }
}
