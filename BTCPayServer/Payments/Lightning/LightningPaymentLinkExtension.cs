#nullable enable
using System;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningPaymentLinkExtension : IPaymentLinkExtension
    {
        public LightningPaymentLinkExtension(PaymentMethodId paymentMethodId, BTCPayNetwork network)
        {
            PaymentMethodId = paymentMethodId;
        }
        public PaymentMethodId PaymentMethodId { get; }
        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var lnInvoiceTrimmedOfScheme = prompt.Destination.ToLowerInvariant()
                .Replace("lightning:", "", StringComparison.InvariantCultureIgnoreCase);
            return $"lightning:{lnInvoiceTrimmedOfScheme}";
        }
    }
}
