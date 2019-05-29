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
        /// <param name="preparePaymentObject"></param>
        /// <returns></returns>
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetworkBase network, object preparePaymentObject);

        /// <summary>
        /// This method called before the rate have been fetched
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="store"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        object PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store, BTCPayNetworkBase network);
        
        bool CanHandle(PaymentMethodId paymentMethodId);
        
        string ToPrettyString(PaymentMethodId paymentMethodId);
    }

    public interface IPaymentMethodHandler<T> : IPaymentMethodHandler where T : ISupportedPaymentMethod
    {
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, BTCPayNetworkBase network, object preparePaymentObject);
    }

    public abstract class PaymentMethodHandlerBase<T> : IPaymentMethodHandler<T> where T : ISupportedPaymentMethod
    {
        public abstract string PrettyDescription { get; }
        public abstract PaymentTypes PaymentType { get; }
        public abstract Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, BTCPayNetworkBase network, object preparePaymentObject);
        public virtual object PreparePayment(T supportedPaymentMethod, StoreData store, BTCPayNetworkBase network)
        {
            return null;
        }

        object IPaymentMethodHandler.PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            if (supportedPaymentMethod is T method)
            {
                return PreparePayment(method, store, network);
            }
            throw new NotSupportedException("Invalid supportedPaymentMethod");
        }

        public bool CanHandle(PaymentMethodId paymentMethodId)
        {
            return paymentMethodId.PaymentType.Equals(PaymentType);
        }

        public string ToPrettyString(PaymentMethodId paymentMethodId)
        {
            return $"{paymentMethodId.CryptoCode} ({PrettyDescription})";
        }

        Task<IPaymentMethodDetails> IPaymentMethodHandler.CreatePaymentMethodDetails(
            ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store,
            BTCPayNetworkBase network, object preparePaymentObject)
        {
            if (supportedPaymentMethod is T method)
            {
                return CreatePaymentMethodDetails(method, paymentMethod, store, network, preparePaymentObject);
            }
            throw new NotSupportedException("Invalid supportedPaymentMethod");
        }
    }
}
