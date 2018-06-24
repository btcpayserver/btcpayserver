using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.Charge;
using BTCPayServer.Payments.Lightning.CLightning;
using BTCPayServer.Payments.Lightning.Lnd;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningClientFactory
    {
        public ILightningInvoiceClient CreateClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            var connString = supportedPaymentMethod.GetLightningUrl();
            if (connString.ConnectionType == LightningConnectionType.Charge)
            {
                return new ChargeClient(connString.UriWithCreds, network.NBitcoinNetwork);
            }
            else if (connString.ConnectionType == LightningConnectionType.CLightning)
            {
                return new CLightningRPCClient(connString.UriPlain, network.NBitcoinNetwork);
            }
            else if (connString.ConnectionType == LightningConnectionType.Lnd)
            {
                var hex = new NBitcoin.DataEncoders.HexEncoder();

                byte[] macaroon = null;
                if (!String.IsNullOrEmpty(connString.Macaroon))
                    macaroon = hex.DecodeData(connString.Macaroon);

                byte[] tls = null;
                if (!String.IsNullOrEmpty(connString.Tls))
                    tls = hex.DecodeData(connString.Tls);

                var swagger = LndSwaggerClientCustomHttp.Create(connString.UriPlain, network.NBitcoinNetwork, tls, macaroon);
                return new LndInvoiceClient(swagger);
            }
            else
                throw new NotSupportedException($"Unsupported connection string for lightning server ({connString.ConnectionType})");
        }
    }
}
