#if ALTCOINS
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    public class ZcashPaymentType : PaymentType
    {
        public static ZcashPaymentType Instance { get; } = new ZcashPaymentType();
        public override string ToPrettyString() => "On-Chain";

        public override string GetId() => "ZcashLike";
        public override string ToStringNormalized()
        {
            return "Zcash";
        }

        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<ZcashLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return JsonConvert.SerializeObject(paymentData);
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Zcash/ViewZcashLikePaymentData";
    }
}
#endif
