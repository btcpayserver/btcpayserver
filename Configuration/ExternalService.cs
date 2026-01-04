using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NBitcoin;

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
                                "LND (gRPC)");
            Load(configuration, cryptoCode, "lndrest", ExternalServiceTypes.LNDRest, "Invalid setting {0}, " + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "lnd server: 'server=https://lnd.example.com;macaroondirectorypath=/root/.lnd;certthumbprint=2abdf302...'" + Environment.NewLine +
                                "Error: {1}",
                                "LND (REST)");
            Load(configuration, cryptoCode, "lndseedbackup", ExternalServiceTypes.LNDSeedBackup, "Invalid setting {0}, " + Environment.NewLine +
                                "lnd seed backup: /etc/merchant_lnd/data/chain/bitcoin/regtest/walletunlock.json'" + Environment.NewLine +
                                "Error: {1}",
                                "LND Seed Backup");
            Load(configuration, cryptoCode, "spark", ExternalServiceTypes.Spark, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/spark/btc/;cookiefile=/etc/clightning_bitcoin_spark/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "Core Lightning (Spark)");
            Load(configuration, cryptoCode, "rtl", ExternalServiceTypes.RTL, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/rtl/btc/;cookiefile=/etc/clightning_bitcoin_rtl/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "Ride The Lightning");
            Load(configuration, cryptoCode, "torq", ExternalServiceTypes.Torq, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/torq/cookie-login/;cookiefile=/etc/lnd_bitcoin_rtl/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "Torq");
            Load(configuration, cryptoCode, "thunderhub", ExternalServiceTypes.ThunderHub, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/thub/;cookiefile=/etc/clightning_bitcoin_rtl/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "ThunderHub");
            Load(configuration, cryptoCode, "clightningrest", ExternalServiceTypes.CLightningRest, "Invalid setting {0}, " + Environment.NewLine +
                                $"Valid example: 'server=https://btcpay.example.com/clightning-rest/btc/;cookiefile=/etc/clightning_bitcoin_rtl/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "Core Lightning (REST)");
            Load(configuration, cryptoCode, "charge", ExternalServiceTypes.Charge, "Invalid setting {0}, " + Environment.NewLine +
                                $"lightning charge server: 'type=charge;server=https://charge.example.com;api-token=2abdf302...'" + Environment.NewLine +
                                $"lightning charge server: 'type=charge;server=https://charge.example.com;cookiefilepath=/root/.charge/.cookie'" + Environment.NewLine +
                                "Error: {1}",
                                "Core Lightning (Charge)");

        }

        public void LoadNonCryptoServices(IConfiguration configuration)
        {
            Load(configuration, null, "configurator", ExternalServiceTypes.Configurator, "Invalid setting {0}, " + Environment.NewLine +
                                                                                   $"configurator: 'cookiefilepathfile=/etc/configurator/cookie'" + Environment.NewLine +
                                                                                   "Error: {1}",
                "Configurator");
        }

        void Load(IConfiguration configuration, string cryptoCode, string serviceName, ExternalServiceTypes type, string errorMessage, string displayName)
        {
            var setting = $"{(!string.IsNullOrEmpty(cryptoCode) ? $"{cryptoCode}." : string.Empty)}external.{serviceName}";
            var connStr = configuration.GetOrDefault<string>(setting, string.Empty);
            if (connStr.Length != 0)
            {
                ExternalConnectionString serviceConnection;
                if (type == ExternalServiceTypes.LNDSeedBackup)
                {
                    // just using CookieFilePath to hold variable instead of refactoring whole holder class to better conform
                    serviceConnection = new ExternalConnectionString { CookieFilePath = connStr };
                }
                else if (!ExternalConnectionString.TryParse(connStr, out serviceConnection, out var error))
                {
                    throw new ConfigException(string.Format(CultureInfo.InvariantCulture, errorMessage, setting, error));
                }
                this.Add(new ExternalService() { Type = type, ConnectionString = serviceConnection, CryptoCode = cryptoCode, DisplayName = displayName, ServiceName = serviceName });
            }
        }

        public ExternalService GetService(string serviceName, string cryptoCode)
        {
            return this.FirstOrDefault(o =>
                (cryptoCode == null && o.CryptoCode == null) ||
                (o.CryptoCode != null && o.CryptoCode.Equals(cryptoCode, StringComparison.OrdinalIgnoreCase))
                &&
                o.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        }

        public static readonly ExternalServiceTypes[] LightningServiceTypes =
        {
            ExternalServiceTypes.Spark,
            ExternalServiceTypes.RTL,
            ExternalServiceTypes.ThunderHub,
            ExternalServiceTypes.Torq
        };

        public static readonly string[] LightningServiceNames =
        {
            "Lightning Terminal"
        };
    }

    public class ExternalService
    {
        public string DisplayName { get; set; }
        public ExternalServiceTypes Type { get; set; }
        public ExternalConnectionString ConnectionString { get; set; }
        public string CryptoCode { get; set; }
        public string ServiceName { get; set; }

        public async Task<string> GetLink(Uri absoluteUriNoPathBase, ChainName networkType)
        {
            var connectionString = await ConnectionString.Expand(absoluteUriNoPathBase, Type, networkType);
            var tokenParam = Type == ExternalServiceTypes.ThunderHub ? "token" : "access-key";
            return $"{connectionString.Server}?{tokenParam}={connectionString.AccessKey}";
        }
    }

    public enum ExternalServiceTypes
    {
        LNDRest,
        LNDGRPC,
        LNDSeedBackup,
        Spark,
        RTL,
        ThunderHub,
        Charge,
        P2P,
        RPC,
        Configurator,
        CLightningRest,
        Torq
    }
}
