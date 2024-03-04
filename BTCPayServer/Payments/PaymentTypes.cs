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
            MoneroPaymentType.Instance, ZcashPaymentType.Instance,
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
        public abstract CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str);
        public abstract string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData);
        public abstract string InvoiceViewPaymentPartialName { get; }

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
    }
}
