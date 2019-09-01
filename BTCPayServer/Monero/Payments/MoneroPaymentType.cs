using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroPaymentType: PaymentType
    {
        public static MoneroPaymentType Instance { get; } = new MoneroPaymentType();
        public override string ToPrettyString()
        {
            return "";
        }

        public override string GetId()
        {
            throw new System.NotImplementedException();
        }

        public override CryptoPaymentData DeserializePaymentData(string str)
        {
         
#pragma warning disable CS0618
            return JsonConvert.DeserializeObject<MoneroLikePaymentData>(str);
#pragma warning restore CS0618
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            throw new System.NotImplementedException();
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            throw new System.NotImplementedException();
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return string.Format(network.BlockExplorerLink, txId);
        }

        public override string InvoiceViewPaymentPartialName { get; }
    }
}
