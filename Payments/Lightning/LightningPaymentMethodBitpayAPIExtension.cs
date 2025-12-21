#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NBitpayClient;

namespace BTCPayServer.Payments.Lightning;

public class LightningPaymentMethodBitpayAPIExtension : IPaymentMethodBitpayAPIExtension
{
    public LightningPaymentMethodBitpayAPIExtension(
        PaymentMethodId paymentMethodId,
        IEnumerable<IPaymentLinkExtension> paymentLinkExtensions)
    {
        PaymentMethodId = paymentMethodId;
        paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
    }

    public PaymentMethodId PaymentMethodId { get; }

    private IPaymentLinkExtension paymentLinkExtension;

    public void PopulateCryptoInfo(Services.Invoices.InvoiceCryptoInfo cryptoInfo, InvoiceResponse dto, PaymentPrompt prompt, IUrlHelper urlHelper)
    {
        cryptoInfo.PaymentUrls = new Services.Invoices.InvoiceCryptoInfo.InvoicePaymentUrls()
        {
            BOLT11 = paymentLinkExtension.GetPaymentLink(prompt, urlHelper)
        };
    }
}
