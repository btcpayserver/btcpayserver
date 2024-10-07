#nullable enable
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using Org.BouncyCastle.Crypto.Modes.Gcm;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public class LNCheckoutModelExtension : ICheckoutModelExtension
    {
        public const string CheckoutBodyComponentName = "LightningCheckoutBody";
        private readonly DisplayFormatter _displayFormatter;
        IPaymentLinkExtension _PaymentLinkExtension;
        public LNCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            BTCPayNetwork network,
            DisplayFormatter displayFormatter,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
            PaymentMethodHandlerDictionary handlers)
        {
            Network = network;
            _displayFormatter = displayFormatter;
            Handlers = handlers;
            PaymentMethodId = paymentMethodId;
            _PaymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
            var isBTC = PaymentTypes.LN.GetPaymentMethodId("BTC") == paymentMethodId;
            DisplayName = isBTC ? "Lightning" : $"Lightning ({Network.DisplayName})";
        }

        public BTCPayNetwork Network { get; }
        public PaymentMethodHandlerDictionary Handlers { get; }
        public PaymentMethodId PaymentMethodId { get; }

        public string DisplayName { get; }

        public string Image => Network.LightningImagePath;
        public string Badge => "⚡";
        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            if (context is not { Handler: LightningLikePaymentHandler handler })
                return;
            var paymentPrompt = context.InvoiceEntity.GetPaymentPrompt(PaymentMethodId);
            if (paymentPrompt is null)
                return;
            context.Model.CheckoutBodyComponentName = CheckoutBodyComponentName;
            context.Model.InvoiceBitcoinUrl = _PaymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            if (context.Model.InvoiceBitcoinUrl is not null)
                context.Model.InvoiceBitcoinUrlQR = $"lightning:{context.Model.InvoiceBitcoinUrl.ToUpperInvariant()?.Substring("LIGHTNING:".Length)}";
            context.Model.PeerInfo = handler.ParsePaymentPromptDetails(paymentPrompt.Details).NodeInfo;
            if (context.StoreBlob.LightningAmountInSatoshi && context.Model.PaymentMethodCurrency == "BTC")
            {
                BitcoinCheckoutModelExtension.PreparePaymentModelForAmountInSats(context.Model, paymentPrompt.Rate, _displayFormatter);
            }
        }
    }
}
