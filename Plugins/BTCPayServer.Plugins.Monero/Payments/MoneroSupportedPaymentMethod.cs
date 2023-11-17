using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroSupportedPaymentMethod : ISupportedPaymentMethod
    {

        public string CryptoCode { get; set; }
        public long AccountIndex { get; set; }
        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, MoneroPaymentType.Instance);
    }
}

