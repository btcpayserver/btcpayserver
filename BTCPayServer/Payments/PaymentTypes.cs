using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Rating;
using BTCPayServer.Shitcoins.Monero.Payments;
using BTCPayServer.Services.Invoices;
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
                case "monerolike":
                    type = MoneroPaymentType.Instance;
                    break;
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
        public abstract string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData);
        public abstract CryptoPaymentData DeserializePaymentData(string str, BTCPayNetworkBase network);
        public abstract IPaymentMethodDetails DeserializePaymentMethodDetails(string str);
        public abstract ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkProvider networkProvider, PaymentMethodId paymentMethodId, JToken value);

        public abstract ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value);

        public abstract string GetTransactionLink(BTCPayNetworkBase network, string txId);
        public abstract string InvoiceViewPaymentPartialName { get; }
        public abstract IEnumerable<CurrencyPair> GetCurrencyPairs(ISupportedPaymentMethod supportedPaymentMethod,
            string targetCurrencyCode, StoreBlob blob);

        public virtual bool IsAvailable(PaymentMethodId paymentMethodId, BTCPayNetworkProvider networkProvider)
        {
            return networkProvider.Support(paymentMethodId.CryptoCode);
        }

        public virtual IPaymentMethodHandler GetPaymentMethodHandler(PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,  PaymentMethodId paymentMethodId)
        {
            return paymentMethodHandlerDictionary[paymentMethodId];
        }
    }
}
