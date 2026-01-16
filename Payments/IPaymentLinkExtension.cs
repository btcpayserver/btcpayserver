#nullable enable
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Payments
{
    public interface IPaymentLinkExtension
    {
        PaymentMethodId PaymentMethodId { get; }
        string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper);
    }
}
