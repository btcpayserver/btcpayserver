using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
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
        /// <param name="logs"></param>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="paymentMethod"></param>
        /// <param name="store"></param>
        /// <param name="network"></param>
        /// <param name="preparePaymentObject"></param>
        /// <param name="invoicePaymentMethods"></param>
        /// <returns></returns>
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs,
            ISupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, BTCPayNetworkBase network, object preparePaymentObject,
            IEnumerable<PaymentMethodId> invoicePaymentMethods);

        /// <summary>
        /// This method called before the rate have been fetched
        /// </summary>
        /// <param name="supportedPaymentMethod"></param>
        /// <param name="store"></param>
        /// <param name="network"></param>
        /// <returns></returns>
        object PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store, BTCPayNetworkBase network);

        void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse, StoreBlob storeBlob,
            IPaymentMethod paymentMethod);
        string GetCryptoImage(PaymentMethodId paymentMethodId);
        string GetPaymentMethodName(PaymentMethodId paymentMethodId);

        IEnumerable<PaymentMethodId> GetSupportedPaymentMethods();
        CheckoutUIPaymentMethodSettings GetCheckoutUISettings();
    }

    public interface IPaymentMethodHandler<TSupportedPaymentMethod, TBTCPayNetwork> : IPaymentMethodHandler
        where TSupportedPaymentMethod : ISupportedPaymentMethod
        where TBTCPayNetwork : BTCPayNetworkBase
    {
        Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs, TSupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, TBTCPayNetwork network, object preparePaymentObject, IEnumerable<PaymentMethodId> invoicePaymentMethods);
    }

    public abstract class PaymentMethodHandlerBase<TSupportedPaymentMethod, TBTCPayNetwork> : IPaymentMethodHandler<
            TSupportedPaymentMethod, TBTCPayNetwork>
        where TSupportedPaymentMethod : ISupportedPaymentMethod
        where TBTCPayNetwork : BTCPayNetworkBase
    {
        public abstract PaymentType PaymentType { get; }

        public abstract Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
            InvoiceLogs logs,
            TSupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod, StoreData store, TBTCPayNetwork network, object preparePaymentObject, IEnumerable<PaymentMethodId> invoicePaymentMethods);

        public abstract void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod);
        public abstract string GetCryptoImage(PaymentMethodId paymentMethodId);
        public abstract string GetPaymentMethodName(PaymentMethodId paymentMethodId);

        public abstract IEnumerable<PaymentMethodId> GetSupportedPaymentMethods();
        public virtual CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Bitcoin/BitcoinLikeMethodCheckout",
                CheckoutBodyVueComponentName = "BitcoinLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "BitcoinLikeMethodCheckoutHeader",
                NoScriptPartialName = "Bitcoin/BitcoinLikeMethodCheckoutNoScript"
            };
        }

        public virtual object PreparePayment(TSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return null;
        }

        public virtual void PreparePaymentModelForAmountInSats(PaymentModel model, IPaymentMethod paymentMethod, DisplayFormatter displayFormatter)
        {
            var satoshiCulture = new CultureInfo(CultureInfo.InvariantCulture.Name)
            {
                NumberFormat = { NumberGroupSeparator = " " }
            };
            model.CryptoCode = "sats";
            model.BtcDue = Money.Parse(model.BtcDue).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
            model.BtcPaid = Money.Parse(model.BtcPaid).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
            model.OrderAmount = Money.Parse(model.OrderAmount).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
            model.NetworkFee = new Money(model.NetworkFee, MoneyUnit.BTC).ToUnit(MoneyUnit.Satoshi);
            model.Rate = model.InvoiceCurrency is "BTC" or "SATS"
                ? null
                : displayFormatter.Currency(paymentMethod.Rate / 100_000_000, model.InvoiceCurrency, DisplayFormatter.CurrencyFormat.Symbol);
        }

        public Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs,
            ISupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, BTCPayNetworkBase network, object preparePaymentObject,
            IEnumerable<PaymentMethodId> invoicePaymentMethods)
        {
            if (supportedPaymentMethod is TSupportedPaymentMethod method && network is TBTCPayNetwork correctNetwork)
            {
                return CreatePaymentMethodDetails(logs, method, paymentMethod, store, correctNetwork, preparePaymentObject, invoicePaymentMethods);
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
    }
}
