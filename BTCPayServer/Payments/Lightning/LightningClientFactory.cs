using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.Charge;
using BTCPayServer.Payments.Lightning.CLightning;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningClientFactory
    {
        public ILightningInvoiceClient CreateClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            var uri = supportedPaymentMethod.GetLightningUrl();
            return CreateClient(uri, network.NBitcoinNetwork);
        }

        public static ILightningInvoiceClient CreateClient(LightningConnectionString uri, Network network)
        {
            if (uri.ConnectionType == LightningConnectionType.Charge)
            {
                return new ChargeClient(uri.ToUri(true), network);
            }
            else if (uri.ConnectionType == LightningConnectionType.CLightning)
            {
                return new CLightningRPCClient(uri.ToUri(false), network);
            }
            else
                throw new NotSupportedException($"Unsupported connection string for lightning server ({uri.ConnectionType})");
        }

        public static ILightningInvoiceClient CreateClient(string connectionString, Network network)
        {
            if (!Payments.Lightning.LightningConnectionString.TryParse(connectionString, false, out var conn, out string error))
                throw new FormatException($"Invalid format ({error})");
            return Payments.Lightning.LightningClientFactory.CreateClient(conn, network);
        }
    }
}
