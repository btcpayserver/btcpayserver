using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.Charge;
using BTCPayServer.Payments.Lightning.CLightning;
using NBitcoin;
using BTCPayServer.Payments.Lightning.Lnd;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningClientFactory
    {
        public ILightningInvoiceClient CreateClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            var uri = supportedPaymentMethod.GetLightningUrl();
            return CreateClient(uri, network.NBitcoinNetwork);
        }

        public static ILightningInvoiceClient CreateClient(LightningConnectionString connString, Network network)
        {
            if (connString.ConnectionType == LightningConnectionType.Charge)
            {
                return new ChargeClient(connString.ToUri(true), network);
            }
            else if (connString.ConnectionType == LightningConnectionType.CLightning)
            {
                return new CLightningRPCClient(connString.ToUri(false), network);
            }
            else if (connString.ConnectionType == LightningConnectionType.LndREST)
            {
                return new LndInvoiceClient(new LndSwaggerClient(new LndRestSettings(connString.BaseUri)
                {
                    Macaroon = connString.Macaroon,
                    CertificateThumbprint = connString.CertificateThumbprint,
                    AllowInsecure = connString.AllowInsecure,
                }));
            }
            else
                throw new NotSupportedException($"Unsupported connection string for lightning server ({connString.ConnectionType})");
        }

        public static ILightningInvoiceClient CreateClient(string connectionString, Network network)
        {
            if (!Payments.Lightning.LightningConnectionString.TryParse(connectionString, false, out var conn, out string error))
                throw new FormatException($"Invalid format ({error})");
            return Payments.Lightning.LightningClientFactory.CreateClient(conn, network);
        }
    }
}
