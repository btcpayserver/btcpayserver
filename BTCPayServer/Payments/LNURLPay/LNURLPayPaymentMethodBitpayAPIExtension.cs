using BTCPayServer.Models;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Payments.LNURLPay;

public class LNURLPayPaymentMethodBitpayAPIExtension : IPaymentMethodBitpayAPIExtension
{
    public LNURLPayPaymentMethodBitpayAPIExtension(
  PaymentMethodId paymentMethodId,
  IEnumerable<IPaymentLinkExtension> paymentLinkExtensions)
    {
        PaymentMethodId = paymentMethodId;
        paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
    }
    public PaymentMethodId PaymentMethodId { get; }

    private IPaymentLinkExtension paymentLinkExtension;

    public void PopulateCryptoInfo(BTCPayServer.Services.Invoices.InvoiceCryptoInfo cryptoInfo, InvoiceResponse dto, PaymentPrompt prompt, IUrlHelper urlHelper)
    {
        var link = paymentLinkExtension.GetPaymentLink(prompt, urlHelper);
        if (link is not null)
        {
            cryptoInfo.PaymentUrls = new BTCPayServer.Services.Invoices.InvoiceCryptoInfo.InvoicePaymentUrls()
            {
                AdditionalData = new Dictionary<string, JToken>()
                {
                    {"LNURLP", JToken.FromObject(link)}
                }
            };
        }
    }
}
