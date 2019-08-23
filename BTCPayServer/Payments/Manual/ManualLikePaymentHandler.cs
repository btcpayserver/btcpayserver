using System.Collections.Generic;
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
    public class ManualLikePaymentHandler : IPaymentMethodHandler
    {
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public ManualLikePaymentHandler(BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }
        
        public Task<IPaymentMethodDetails> CreatePaymentMethodDetails(ISupportedPaymentMethod supportedPaymentMethod,
            PaymentMethod paymentMethod, string invoiceCurrencyCode,
            StoreData store, BTCPayNetworkBase network, object preparePaymentObject)
        {
            paymentMethod.SetId(new PaymentMethodId(invoiceCurrencyCode, PaymentTypes.Manual));
            return Task.FromResult((IPaymentMethodDetails)new ManualPaymentMethod());
        }

        public object PreparePayment(ISupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return null;
        }

        public void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse, StoreData storeData,
            StoreBlob storeBlob, PaymentMethodAccounting accounting)
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

        public PaymentMethod GetPaymentMethodInInvoice(InvoiceEntity invoice, PaymentMethodId paymentMethodId)
        {
            return invoice.GetPaymentMethods().FirstOrDefault(method => method.GetId().PaymentType == ManualPaymentType.Instance);
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
