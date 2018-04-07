using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// This class customize invoice creation by the creation of payment details for the PaymentMethod during invoice creation
    /// </summary>
    public interface IPaymentMethodHandler
    {
        /// <summary>
        /// Create needed to track payments of this invoice
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="paymentMethod"></param>
        /// <param name="store"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network);
    }

    public interface IPaymentMethodHandler<T> : IPaymentMethodHandler where T : ISupportedPaymentMethod
    {
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network);
    }

    public abstract class PaymentMethodHandlerBase<T> : IPaymentMethodHandler<T> where T : ISupportedPaymentMethod
    {
        public abstract Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network);

        Task<IPaymentMethodDetails> IPaymentMethodHandler.CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network)
        {
            if (supportedPaymentMethod is T method)
            {
                return CreatePaymentMethodDetails(method, paymentMethod, store, network);
            }
            throw new NotSupportedException("Invalid supportedPaymentMethod");
        }
    }
}
