using System;
#if ALTCOINS
using BTCPayServer.Services.Altcoins.Monero.Payments;
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
        /// <summary>
        /// On-Chain UTXO based, bitcoin compatible
        /// </summary>
        public static BitcoinPaymentType BTCLike => BitcoinPaymentType.Instance;
        /// <summary>
        /// Lightning payment
        /// </summary>
        public static LightningPaymentType LightningLike => LightningPaymentType.Instance;

        public static bool TryParse(string paymentType, out PaymentType type)
        {
            switch (paymentType.ToLowerInvariant())
            {
                case "btclike":
                case "onchain":
                    type = PaymentTypes.BTCLike;
                    break;
                case "lightninglike":
                case "offchain":
                    type = PaymentTypes.LightningLike;
                    break;
#if ALTCOINS
                case "monerolike":
                    type = MoneroPaymentType.Instance;
                    break;
#endif
                default:
                    type = null;
                    return false;
            }
            return true;
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

        public abstract string GetId();
        public abstract CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str);
        public abstract string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData);
        public abstract IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str);
        public abstract string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details);
        public abstract ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value);
        public abstract string GetTransactionLink(BTCPayNetworkBase network, string txId);
        public abstract string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails,
            Money cryptoInfoDue, string serverUri);
        public abstract string InvoiceViewPaymentPartialName { get; }
    }
}
