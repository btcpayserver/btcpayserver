using System.Threading.Tasks;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Payments
{
    public record PaymentModelContext(
        PaymentModel Model,
        Data.StoreData Store,
        Data.StoreBlob StoreBlob,
        InvoiceEntity InvoiceEntity,
        IUrlHelper UrlHelper,
        PaymentPrompt Prompt,
        IPaymentMethodHandler Handler);
    public interface IPaymentModelExtension
    {
        public PaymentMethodId PaymentMethodId { get; }
        string DisplayName { get; }
        string Image { get; }
        string Badge { get; }
        Task ModifyPaymentModel(PaymentModelContext context);
    }
}
