using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// This class customize invoice creation by the creation of payment details for the PaymentMethod during invoice creation
    /// </summary>
    public interface IPaymentMethodHandler
    {
        /// <summary>
        /// Returns true if the dependencies for a specific payment method are satisfied. 
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="network"></param>
        /// <returns>true if this payment method is available</returns>
        bool IsAvailable(ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network);

        /// <summary>
        /// Create needed to track payments of this invoice
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="paymentMethod"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network);
    }

    public interface IPaymentMethodHandler<T> : IPaymentMethodHandler where T : ISupportedPaymentMethod
    {
        bool IsAvailable(T supportedPaymentMethod, BTCPayNetwork network);
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network);
    }

    public abstract class PaymentMethodHandlerBase<T> : IPaymentMethodHandler<T> where T : ISupportedPaymentMethod
    {
        public abstract Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network);

        Task<IPaymentMethodDetails> IPaymentMethodHandler.CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network)
        {
            if (supportedPaymentMethod is T method)
            {
                return CreatePaymentMethodDetails(method, paymentMethod, network);
            }
            throw new NotSupportedException("Invalid supportedPaymentMethod");
        }

        public abstract bool IsAvailable(T supportedPaymentMethod, BTCPayNetwork network);

        bool IPaymentMethodHandler.IsAvailable(ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            if(supportedPaymentMethod is T method)
            { 
                return IsAvailable(method, network);
            }
            return false;
        }
    }
}
