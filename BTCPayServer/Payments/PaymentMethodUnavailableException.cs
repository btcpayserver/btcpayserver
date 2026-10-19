using System;

namespace BTCPayServer.Payments
{
    public class PaymentMethodUnavailableException : Exception
    {
        public bool IsSafeToExpose { get; }

        public PaymentMethodUnavailableException(string message) : base(message)
        {

        }
        public PaymentMethodUnavailableException(string message, bool isSafeToExpose) : base(message)
        {
            IsSafeToExpose = isSafeToExpose;
        }
        public PaymentMethodUnavailableException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}
