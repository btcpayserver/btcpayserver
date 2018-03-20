using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.Charge;
using BTCPayServer.Payments.Lightning.CLightning;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningClientFactory
    {
        public ILightningInvoiceClient CreateClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            var uri = supportedPaymentMethod.GetLightningUrl();
            if (uri.ConnectionType == LightningConnectionType.Charge)
            {
                return new ChargeClient(uri.ToUri(true), network.NBitcoinNetwork);
            }
            else if (uri.ConnectionType == LightningConnectionType.CLightning)
            {
                return new CLightningRPCClient(uri.ToUri(false), network.NBitcoinNetwork);
            }
            else
                throw new NotSupportedException($"Unsupported connection string for lightning server ({uri.ConnectionType})");
        }
    }
}
