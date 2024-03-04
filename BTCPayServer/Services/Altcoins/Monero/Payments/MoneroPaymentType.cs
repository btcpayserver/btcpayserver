#if ALTCOINS
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroPaymentType : PaymentType
    {
        public static MoneroPaymentType Instance { get; } = new MoneroPaymentType();
        public override string ToPrettyString() => "On-Chain";

        public override string GetId() => "MoneroLike";
        public override string ToStringNormalized()
        {
            return "Monero";
        }

        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<MoneroLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return JsonConvert.SerializeObject(paymentData);
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Monero/ViewMoneroLikePaymentData";
    }
}
#endif
