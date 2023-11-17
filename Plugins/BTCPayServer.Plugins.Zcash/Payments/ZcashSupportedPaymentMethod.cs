using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Zcash.Payments
{
    public class ZcashSupportedPaymentMethod : ISupportedPaymentMethod
    {

        public string CryptoCode { get; set; }
        public long AccountIndex { get; set; }
        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, ZcashPaymentType.Instance);
    }
}

