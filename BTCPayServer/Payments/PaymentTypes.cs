using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

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
        public abstract CryptoPaymentData DeserializePaymentData(string cryptoPaymentData);
    }
}
