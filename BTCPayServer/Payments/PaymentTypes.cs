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
    /// <summary>
    /// The different ways to pay an invoice
    /// </summary>
    public static class PaymentTypes
    {
        private static PaymentType[] _paymentTypes =
        {
            BTCLike, LightningLike, LNURLPay,
#if ALTCOINS
            MoneroLike, ZcashLike,
#endif
        };
        /// <summary>
        /// On-Chain UTXO based, bitcoin compatible
        /// </summary>
        public static BitcoinPaymentType BTCLike => BitcoinPaymentType.Instance;
        /// <summary>
        /// Lightning payment
        /// </summary>
        public static LightningPaymentType LightningLike => LightningPaymentType.Instance;
        /// <summary>
        /// Lightning payment
        /// </summary>
        public static LNURLPayPaymentType LNURLPay => LNURLPayPaymentType.Instance;

#if ALTCOINS
        /// <summary>
        /// Monero payment
        /// </summary>
        public static MoneroPaymentType MoneroLike => MoneroPaymentType.Instance;
        /// <summary>
        /// Zcash payment
        /// </summary>
        public static ZcashPaymentType ZcashLike => ZcashPaymentType.Instance;
#endif

        public static bool TryParse(string paymentType, out PaymentType type)
        {
            type = _paymentTypes.FirstOrDefault(type1 => type1.IsPaymentType(paymentType));
            return type != null;
        }
        public static PaymentType Parse(string paymentType)
        {
            if (!TryParse(paymentType, out var result))
                throw new FormatException("Invalid payment type");
            return result;
        }
    }

    public abstract class PaymentType
    {
        public abstract string ToPrettyString();
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
        public abstract string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails,
            decimal cryptoInfoDue, string serverUri);
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
