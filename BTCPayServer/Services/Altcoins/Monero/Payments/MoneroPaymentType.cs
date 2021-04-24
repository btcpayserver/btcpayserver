#if ALTCOINS
using System.Globalization;
using BTCPayServer.Payments;
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

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<MoneroLikeOnChainPaymentMethodDetails>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return JsonConvert.SerializeObject(details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<MoneroSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails, Money cryptoInfoDue, string serverUri)
        {
            return paymentMethodDetails.Activated
                ? $"{(network as MoneroLikeSpecificBtcPayNetwork).UriScheme}:{paymentMethodDetails.GetPaymentDestination()}?tx_amount={cryptoInfoDue.ToDecimal(MoneyUnit.BTC)}"
                : string.Empty;
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Monero/ViewMoneroLikePaymentData";
    }
}
#endif
