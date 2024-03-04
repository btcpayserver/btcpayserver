using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments.Lightning;
using NBitcoin;

namespace BTCPayServer.Payments.LNURLPay
{
    public class LNURLPayPaymentModelExtension : IPaymentModelExtension
    {
        public LNURLPayPaymentModelExtension(
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
            DisplayName = isBTC ? "Lightning (via LNURL)" : $"Lightning ({network.DisplayName} via LNURL)";
        }
        public PaymentMethodId PaymentMethodId { get; }

        private BTCPayNetwork _network;
        private readonly DisplayFormatter _displayFormatter;
        private readonly IPaymentLinkExtension paymentLinkExtension;
        private readonly LNURLPayPaymentHandler handler;

        public string DisplayName { get; }
        public string Image => _network.LightningImagePath;
        public string Badge => "⚡";

        private const string UriScheme = "lightning:";
        public void ModifyPaymentModel(PaymentModelContext context)
        {
            var lnurl = paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            if (lnurl is not null)
            {
                context.Model.BtcAddress = lnurl.Replace(UriScheme, "");
                context.Model.InvoiceBitcoinUrl = lnurl;
                context.Model.InvoiceBitcoinUrlQR = lnurl.ToUpperInvariant().Replace(UriScheme.ToUpperInvariant(), UriScheme);
            }
            context.Model.PeerInfo = handler.ParsePaymentPromptDetails(context.Prompt.Details).NodeInfo;
            if (context.StoreBlob.LightningAmountInSatoshi && context.Model.CryptoCode == "BTC")
            {
                BitcoinPaymentModelExtension.PreparePaymentModelForAmountInSats(context.Model, context.Prompt.Rate, _displayFormatter);
            }
        }
    }
}
