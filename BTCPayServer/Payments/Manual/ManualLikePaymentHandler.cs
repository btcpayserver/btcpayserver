using System.Collections.Generic;
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
    }

    public class ManualLikePaymentHandler : IPaymentMethodHandler
    {
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

        public void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse)
        {
            model.IsLightning = false;
            model.PaymentMethodName = "Manual";
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
