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

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<ZcashLikeOnChainPaymentMethodDetails>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return JsonConvert.SerializeObject(details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<ZcashSupportedPaymentMethod>(value.ToString());
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails, decimal cryptoInfoDue, string serverUri)
        {
            return paymentMethodDetails.Activated
                ? $"{(network as ZcashLikeSpecificBtcPayNetwork).UriScheme}:{paymentMethodDetails.GetPaymentDestination()}?amount={cryptoInfoDue}"
                : string.Empty;
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Zcash/ViewZcashLikePaymentData";
        public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore)
        {
            if (supportedPaymentMethod is ZcashSupportedPaymentMethod ZcashSupportedPaymentMethod)
            {
                return new
                {
                    ZcashSupportedPaymentMethod.AccountIndex,
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
