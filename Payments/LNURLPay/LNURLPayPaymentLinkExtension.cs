#nullable enable
using BTCPayServer.Abstractions.Extensions;
using System;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Payments.Lightning;
using NBitcoin;

namespace BTCPayServer.Payments.LNURLPay
{
    public class LNURLPayPaymentLinkExtension : IPaymentLinkExtension
    {
        private readonly BTCPayNetwork _network;
        private readonly PaymentMethodHandlerDictionary _handlers;

        public LNURLPayPaymentLinkExtension(
            PaymentMethodId paymentMethodId,
            BTCPayNetwork network,
            PaymentMethodHandlerDictionary handlers)
        {
            _network = network;
            _handlers = handlers;
            PaymentMethodId = paymentMethodId;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string? GetPaymentLink(PaymentPrompt paymentPrompt, IUrlHelper? urlHelper)
        {
            if (!_handlers.TryGetValue(PaymentMethodId, out var o) || o is not LNURLPayPaymentHandler handler)
                return null;
            try
            {
                var lnurlPaymentMethodDetails = handler.ParsePaymentPromptDetails(paymentPrompt.Details);
                var link = urlHelper?.ActionLink(nameof(UILNURLController.GetLNURLForInvoice), "UILNURL", new { invoiceId = paymentPrompt.ParentEntity.Id, cryptoCode = _network.CryptoCode });
                if (link is null)
                    return null;
                var uri = new Uri(link, UriKind.Absolute);
                return LNURL.LNURL.EncodeUri(uri, "payRequest", lnurlPaymentMethodDetails.Bech32Mode).ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
