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
        PaymentType GetPaymentType();
        /// <summary>
        /// Returns fee that the merchant charge to the customer for the next payment
        /// </summary>
        /// <returns></returns>
        decimal GetNextNetworkFee();
        /// <summary>
        /// Returns recommended fee rate for a transaction
        /// </summary>
        /// <returns></returns>
        decimal GetFeeRate();
        /// <summary>
        /// Change the payment destination (internal plumbing)
        /// </summary>
        /// <param name="newPaymentDestination"></param>
        void SetPaymentDestination(string newPaymentDestination);
    }
}
