#if ALTCOINS
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Chia.Payments
{
    public class ChiaPaymentType : PaymentType
    {
        public static ChiaPaymentType Instance { get; } = new ChiaPaymentType();
        public override string ToPrettyString() => "On-Chain";

        public override string GetId() => "ChiaLike";
        public override string ToStringNormalized()
        {
            return "Chia";
        }

        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<ChiaLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return JsonConvert.SerializeObject(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<ChiaLikeOnChainPaymentMethodDetails>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return JsonConvert.SerializeObject(details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<ChiaSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails, decimal cryptoInfoDue, string serverUri)
        {
            return paymentMethodDetails.Activated
                ? $"chia:{paymentMethodDetails.GetPaymentDestination()}?amount={cryptoInfoDue}"
                : string.Empty;
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Chia/ViewChiaLikePaymentData";
        public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore)
        {
            if (supportedPaymentMethod is ChiaSupportedPaymentMethod ChiaSupportedPaymentMethod)
            {
                return new
                {
                    WalletId = ChiaSupportedPaymentMethod.WalletId,
                };
            }

            return null;
        }

        public override void PopulateCryptoInfo(InvoiceEntity invoice, PaymentMethod details, InvoiceCryptoInfo invoiceCryptoInfo, string serverUrl)
        {
            
        }
    }
}
#endif
