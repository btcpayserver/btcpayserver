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
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            return value.ToObject<ManualPaymentSettings>();
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return "N/A";
        }
    }
}
