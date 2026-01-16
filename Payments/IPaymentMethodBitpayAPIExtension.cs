using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Payments
{
    public interface IPaymentMethodBitpayAPIExtension
    {
        PaymentMethodId PaymentMethodId { get; }
        void PopulateCryptoInfo(BTCPayServer.Services.Invoices.InvoiceCryptoInfo cryptoInfo, InvoiceResponse dto, PaymentPrompt prompt, IUrlHelper urlHelper);
    }
}
