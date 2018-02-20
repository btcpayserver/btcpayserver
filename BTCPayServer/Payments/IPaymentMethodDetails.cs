using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// Represent information necessary to track a payment
    /// </summary>
    public interface IPaymentMethodDetails
    {
        /// <summary>
        /// A string representation of the payment destination
        /// </summary>
        /// <returns></returns>
        string GetPaymentDestination();
        PaymentTypes GetPaymentType();
        /// <summary>
        /// Returns what a merchant would need to pay to cashout this payment
        /// </summary>
        /// <returns></returns>
        decimal GetTxFee();
        void SetNoTxFee();
        /// <summary>
        /// Change the payment destination (internal plumbing)
        /// </summary>
        /// <param name="newPaymentDestination"></param>
        void SetPaymentDestination(string newPaymentDestination);
    }
}
