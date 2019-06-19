using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class ManualPaymentType : PaymentType
    {
        public static ManualPaymentType Instance { get; } = new ManualPaymentType();
        private ManualPaymentType()
        {

        }

        public override string ToPrettyString() => "Manual";
        public override string GetId() => "Manual";

        public override CryptoPaymentData DeserializePaymentData(string str)
        {
            return JsonConvert.DeserializeObject<ManualPaymentData>(str);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<ManualPaymentMethod>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            var net = (BTCPayNetwork)network;
            if (value is JObject jobj)
            {
                var scheme = net.NBXplorerNetwork.Serializer.ToObject<DerivationSchemeSettings>(jobj);
                scheme.Network = net;
                return scheme;
            }
            // Legacy
            return DerivationSchemeSettings.Parse(((JValue)value).Value<string>(), net);
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            if (txId == null)
                throw new ArgumentNullException(nameof(txId));
            if (network?.BlockExplorerLink == null)
                return null;
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }
    }
}
