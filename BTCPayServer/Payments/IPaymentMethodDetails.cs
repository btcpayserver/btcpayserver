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
        /// Returns fee that the merchant charge to the customer for the next payment
        /// </summary>
        /// <returns></returns>
        decimal GetNextNetworkFee();
        /// <summary>
        /// Change the payment destination (internal plumbing)
        /// </summary>
        /// <param name="newPaymentDestination"></param>
        void SetPaymentDestination(string newPaymentDestination);
    }
}
