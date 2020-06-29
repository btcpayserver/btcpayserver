using System;

namespace BTCPayServer.Payments
{
    public class PaymentMethodUnavailableException : Exception
    {
        public PaymentMethodUnavailableException(string message) : base(message)
        {

        }
        public PaymentMethodUnavailableException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}
