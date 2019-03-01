using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Configuration
{
    public class ExternalServices : List<ExternalService>
    {
        public void Load(string cryptoCode, IConfiguration configuration)
        {
            Load(configuration, cryptoCode, "lndgrpc", ExternalServiceTypes.LNDGRPC, "Invalid setting {0}, " + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroondirectorypath=/root/.lnd;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "Error: {1}",
                                "LND (gRPC server)");
            Load(configuration, cryptoCode, "lndrest", ExternalServiceTypes.LNDRest, "Invalid setting {0}, " + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroondirectorypath=/root/.lnd;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "Error: {1}",
                                "LND (REST server)");
            Load(configuration, cryptoCode, "spark", ExternalServiceTypes.Spark, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/spark/btc/;cookiefile=/etc/clightning_bitcoin_spark/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "C-Lightning (Spark server)");
            Load(configuration, cryptoCode, "rtl", ExternalServiceTypes.RTL, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/rtl/btc/;cookiefile=/etc/clightning_bitcoin_rtl/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "LND (Ride the Lightning server)");
            Load(configuration, cryptoCode, "charge", ExternalServiceTypes.Charge, "Invalid setting {0}, " + Environment.NewLine +
                                $"lightning charge server: 'type=charge;server=https://charge.example.com;api-token=2abdf302...'" + Environment.NewLine +
                                $"lightning charge server: 'type=charge;server=https://charge.example.com;cookiefilepath=/root/.charge/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "C-Lightning (Charge server)");
        }

        void Load(IConfiguration configuration, string cryptoCode, string serviceName, ExternalServiceTypes type, string errorMessage, string displayName)
        {
            var setting = $"{cryptoCode}.external.{serviceName}";
            var connStr = configuration.GetOrDefault<string>(setting, string.Empty);
            if (connStr.Length != 0)
            {
                if (!ExternalConnectionString.TryParse(connStr, out var connectionString, out var error))
                {
                    throw new ConfigException(string.Format(CultureInfo.InvariantCulture, errorMessage, setting, error));
                }
                this.Add(new ExternalService() { Type = type, ConnectionString = connectionString, CryptoCode = cryptoCode, DisplayName = displayName, ServiceName = serviceName });
            }
        }

        public ExternalService GetService(string serviceName, string cryptoCode)
        {
            return this.FirstOrDefault(o => o.CryptoCode.Equals(cryptoCode, StringComparison.OrdinalIgnoreCase) &&
                                                                                    o.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class ExternalService
    {
        public string DisplayName { get; set; }
        public ExternalServiceTypes Type { get; set; }
        public ExternalConnectionString ConnectionString { get; set; }
        public string CryptoCode { get; set; }
        public string ServiceName { get; set; }
    }

    public enum ExternalServiceTypes
    {
        LNDRest,
        LNDGRPC,
        Spark,
        RTL,
        Charge
    }
}
