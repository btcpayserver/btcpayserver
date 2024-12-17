using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments.Lightning;
using NBitcoin;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Payments.LNURLPay
{
    public class LNURLCheckoutModelExtension : ICheckoutModelExtension
    {
        public LNURLCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            BTCPayNetwork network,
            DisplayFormatter displayFormatter,
            PaymentMethodHandlerDictionary handlers,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
            _displayFormatter = displayFormatter;
            paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
            handler = (LNURLPayPaymentHandler)handlers[PaymentMethodId];
            var isBTC = PaymentTypes.LNURL.GetPaymentMethodId("BTC") == paymentMethodId;
        }
        public PaymentMethodId PaymentMethodId { get; }

        private BTCPayNetwork _network;
        private readonly DisplayFormatter _displayFormatter;
        private readonly IPaymentLinkExtension paymentLinkExtension;
        private readonly LNURLPayPaymentHandler handler;

        public string Image => _network.LightningImagePath;
        public string Badge => "⚡";

        private const string UriScheme = "lightning:";
        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            if (context is not { Handler: LNURLPayPaymentHandler handler })
                return;
            var lnurl = paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            if (lnurl is not null)
            {
                context.Model.Address = lnurl.Replace(UriScheme, "");
                context.Model.InvoiceBitcoinUrl = lnurl;
                context.Model.InvoiceBitcoinUrlQR = lnurl.ToUpperInvariant().Replace(UriScheme.ToUpperInvariant(), UriScheme);
            }
            context.Model.CheckoutBodyComponentName = LNCheckoutModelExtension.CheckoutBodyComponentName;
            context.Model.PeerInfo = handler.ParsePaymentPromptDetails(context.Prompt.Details).NodeInfo;
            if (context.Model.PaymentMethodCurrency == "BTC")
            {
                switch (context.StoreBlob.LightningCurrencyDisplayMode)
                {
                    case LightningCurrencyDisplayMode.Satoshis:
                        BitcoinCheckoutModelExtension.PreparePaymentModelForAmountInSats(context.Model, context.Prompt.Rate, _displayFormatter);
                            break;
                    case LightningCurrencyDisplayMode.BTC:
                            break;
                    case LightningCurrencyDisplayMode.Fiat:
                        BitcoinCheckoutModelExtension.PreparePaymentModelForAmountInFiat(context.Model, context.Prompt.Rate, _displayFormatter);
                            break;
                }
            }
        }
    }
}
