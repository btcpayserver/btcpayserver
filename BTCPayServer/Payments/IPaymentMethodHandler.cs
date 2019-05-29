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
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, BTCPayNetworkBase network, object preparePaymentObject);

        /// <summary>
        /// This method called before the rate have been fetched
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="store"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        object PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store, BTCPayNetwork network);
        
        bool CanHandle(PaymentMethodId paymentMethodId);
        
        string ToPrettyString(PaymentMethodId paymentMethodId);
    }

    public interface IPaymentMethodHandler<TSupportedPaymentMethod, TBTCPayNetwork> : IPaymentMethodHandler
        where TSupportedPaymentMethod : ISupportedPaymentMethod
        where TBTCPayNetwork : BTCPayNetworkBase
    {
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(TSupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, TBTCPayNetwork network, object preparePaymentObject);
    }

    public abstract class
        PaymentMethodHandlerBase<TSupportedPaymentMethod, TBTCPayNetwork> : IPaymentMethodHandler<
            TSupportedPaymentMethod, TBTCPayNetwork>
        where TSupportedPaymentMethod : ISupportedPaymentMethod
        where TBTCPayNetwork : BTCPayNetworkBase
    {
        public abstract string PrettyDescription { get; }
        public abstract PaymentTypes PaymentType { get; }
        public abstract Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject);
        public virtual object PreparePayment(T supportedPaymentMethod, StoreData store, BTCPayNetwork network)
        {
            return null;
        }

        public Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, BTCPayNetworkBase network, object preparePaymentObject)
        {
            if (supportedPaymentMethod is TSupportedPaymentMethod method && network is TBTCPayNetwork correctNetwork)
            {
                return CreatePaymentMethodDetails(method, paymentMethod, store, correctNetwork, preparePaymentObject);
            }

            throw new NotSupportedException("Invalid supportedPaymentMethod");
        }

        object IPaymentMethodHandler.PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            if (supportedPaymentMethod is TSupportedPaymentMethod method)
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
    }
}
