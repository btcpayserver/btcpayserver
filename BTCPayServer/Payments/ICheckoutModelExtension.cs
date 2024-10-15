using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Payments
{
    public record CheckoutModelContext(
        CheckoutModel Model,
        Data.StoreData Store,
        Data.StoreBlob StoreBlob,
        InvoiceEntity InvoiceEntity,
        IUrlHelper UrlHelper,
        PaymentPrompt Prompt,
        IPaymentMethodHandler Handler);
    public interface ICheckoutModelExtension
    {
        public PaymentMethodId PaymentMethodId { get; }
        string DisplayName { get; }
        string Image { get; }
        string Badge { get; }
        void ModifyCheckoutModel(CheckoutModelContext context);
    }
}
