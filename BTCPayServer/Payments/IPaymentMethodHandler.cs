﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBitpayClient;
using InvoiceResponse = BTCPayServer.Models.InvoiceResponse;

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
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject);

        /// <summary>
        /// This method called before the rate have been fetched
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="store"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        object PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store, BTCPayNetwork network);
        
        bool CanHandle(PaymentMethodId paymentMethodId);

        void PrepareInvoiceDto(InvoiceResponse invoiceResponse, InvoiceEntity invoiceEntity,
            InvoiceCryptoInfo invoiceCryptoInfo,
            PaymentMethodAccounting accounting, PaymentMethod info);
        
        
        string ToPrettyString(PaymentMethodId paymentMethodId);

        void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse);
        string GetCryptoImage(PaymentMethodId paymentMethodId);
        string GetPaymentMethodName(PaymentMethodId paymentMethodId);

        Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob,
            Dictionary<CurrencyPair, Task<RateResult>> rate,
            Money amount, PaymentMethodId paymentMethodId);
        
        IEnumerable<PaymentMethodId> GetSupportedPaymentMethods();

        CryptoPaymentData GetCryptoPaymentData(PaymentEntity paymentEntity);
    }

    public interface IPaymentMethodHandler<T> : IPaymentMethodHandler where T : ISupportedPaymentMethod
    {
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject);
    }

    public abstract class PaymentMethodHandlerBase<T> : IPaymentMethodHandler<T> where T : ISupportedPaymentMethod
    {
        public abstract string PrettyDescription { get; }
        public abstract PaymentTypes PaymentType { get; }

        public abstract Task<IPaymentMethodDetails> CreatePaymentMethodDetails(T supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject);

        public abstract void PrepareInvoiceDto(InvoiceResponse invoiceResponse, InvoiceEntity invoiceEntity,
            InvoiceCryptoInfo invoiceCryptoInfo, PaymentMethodAccounting accounting, PaymentMethod info);

        public abstract void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse);
        public abstract string GetCryptoImage(PaymentMethodId paymentMethodId);
        public abstract string GetPaymentMethodName(PaymentMethodId paymentMethodId);
        public abstract Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob,
            Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount, PaymentMethodId paymentMethodId);
        public abstract IEnumerable<PaymentMethodId> GetSupportedPaymentMethods();
        public abstract CryptoPaymentData GetCryptoPaymentData(PaymentEntity paymentEntity);


        public virtual object PreparePayment(T supportedPaymentMethod, StoreData store, BTCPayNetwork network)
        {
            return null;
        }

        object IPaymentMethodHandler.PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store, BTCPayNetwork network)
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

        Task<IPaymentMethodDetails> IPaymentMethodHandler.CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject)
        {
            if (supportedPaymentMethod is T method)
            {
                return CreatePaymentMethodDetails(method, paymentMethod, store, network, preparePaymentObject);
            }
            throw new NotSupportedException("Invalid supportedPaymentMethod");
        }
    }
}
