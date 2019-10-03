using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroPaymentType: PaymentType
    {
        public static MoneroPaymentType Instance { get; } = new MoneroPaymentType();
        public override string ToPrettyString() => "On-Chain";

        public override string GetId()=> "MoneroLike";


        public override CryptoPaymentData DeserializePaymentData(string str)
        {
         
#pragma warning disable CS0618
            return JsonConvert.DeserializeObject<MoneroLikePaymentData>(str);
#pragma warning restore CS0618
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<MoneroLikeOnChainPaymentMethodDetails>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<MoneroSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }

        public override string InvoiceViewPaymentPartialName { get; }= "Monero/ViewMoneroLikePaymentData";
    }
}
