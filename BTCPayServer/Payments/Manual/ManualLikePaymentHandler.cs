﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Payments.Bitcoin
{
    public class ManualPaymentSettings : ISupportedPaymentMethod
    {
        public PaymentMethodId PaymentId { get; } = StaticPaymentId;
        public static PaymentMethodId StaticPaymentId { get; } = new PaymentMethodId(string.Empty, PaymentTypes.Manual);

        public string DisplayText { get; set; } = string.Empty;
        public bool AllowCustomerToMarkPaid { get; set; } = false;
        public bool AllowPartialPaymentInput { get; set; } = false;
        public bool AllowPaymentNote { get; set; } = false;
        public bool SetPaymentAsConfirmed { get; set; } = true;

    }

    public class ManualLikePaymentHandler : IPaymentMethodHandler
    {
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public ManualLikePaymentHandler(BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }
        
        public Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod,
            StoreData store, BTCPayNetworkBase network, object preparePaymentObject)
        {
            return Task.FromResult((IPaymentMethodDetails)new ManualPaymentMethod());
        }

        public object PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return null;
        }

        public void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse, StoreData storeData,  StoreBlob storeBlob)
        {
            var settings = storeData.GetSupportedPaymentMethods(_BtcPayNetworkProvider).OfType<ManualPaymentSettings>().First();
            model.IsLightning = false;
            model.PaymentMethodName = "Manual";
            model.AdditionalSettings.Add($"Manual", settings);
        }

        public CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return  new CheckoutUIPaymentMethodSettings()
            {
                NoScriptPartialName = "ManualMethodCheckoutNoScript",
                CheckoutBodyVueComponentName = "manual-method-checkout",
                ExtensionPartial = "ManualMethodCheckout"
            };
        }

        public class CheckoutUIPaymentMethodSettings
        {
            public string ExtensionPartial { get; set; }
            public string CheckoutBodyVueComponentName { get; set; }
            public string NoScriptPartialName { get; set; }
        }
        
        public string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            return "imlegacy/manual.svg";
        }

        public string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            return "Manual";
        }

        public Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob,
            Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount,
            PaymentMethodId paymentMethodId)
        {
            return Task.FromResult<string>(null);
        }

        public IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return new[] {ManualPaymentSettings.StaticPaymentId};
        }
    }
}
