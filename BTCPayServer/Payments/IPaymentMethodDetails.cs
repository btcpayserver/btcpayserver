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

        bool Activated {get;set;}
    }
}
