using System;
using System.Linq;
#if ALTCOINS
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
#endif
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public abstract class PaymentType
    {
        public abstract string ToPrettyString();

        public virtual string GetPaymentMethodId(PaymentMethodId paymentMethodId)
        {
            if (paymentMethodId.PaymentType == BitcoinPaymentType.Instance)
                return paymentMethodId.CryptoCode;
#if ALTCOINS
            if (paymentMethodId.CryptoCode == "XMR" && paymentMethodId.PaymentType == MoneroPaymentType.Instance)
                return paymentMethodId.CryptoCode;
            if ((paymentMethodId.CryptoCode == "YEC" || paymentMethodId.CryptoCode == "ZEC") && paymentMethodId.PaymentType == ZcashPaymentType.Instance)
                return paymentMethodId.CryptoCode;
#endif
            return $"{paymentMethodId.CryptoCode}-{paymentMethodId.PaymentType.ToStringNormalized()}";
        }
        public override string ToString()
        {
            return GetId();
        }

        /// <summary>
        /// A string we can expose to Greenfield API, not subjected to internal legacy
        /// </summary>
        /// <returns></returns>
        public virtual string ToStringNormalized()
        {
            return ToString();
        }

        public abstract string GetId();
        public virtual string GetBadge() => null;
        public abstract CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str);
        public abstract string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData);
        public abstract IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str);
        public abstract string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details);
        public abstract ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value);
        public abstract string GetTransactionLink(BTCPayNetworkBase network, string txId);
        public abstract string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails,
            Money cryptoInfoDue, string serverUri);
        public abstract string InvoiceViewPaymentPartialName { get; }

        public abstract object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore);

        public virtual bool IsPaymentType(string paymentType)
        {
            return IsPaymentTypeBase(paymentType);
        }

        protected bool IsPaymentTypeBase(string paymentType)
        {
            paymentType = paymentType?.ToLowerInvariant();
            return new[]
            {
                GetId().Replace("-", "", StringComparison.InvariantCulture),
                ToStringNormalized()
            }.Contains(
                paymentType,
                StringComparer.InvariantCultureIgnoreCase);
        }

        public abstract void PopulateCryptoInfo(InvoiceEntity invoice, PaymentMethod details, Services.Invoices.InvoiceCryptoInfo invoiceCryptoInfo,
            string serverUrl);
    }
}
