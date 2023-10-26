#if ALTCOINS
using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.Payments
{
    public class ChiaSupportedPaymentMethod : ISupportedPaymentMethod
    {

        public string CryptoCode { get; set; }
        public int WalletId { get; set; }
        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, ChiaPaymentType.Instance);
    }
}
#endif
