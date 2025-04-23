using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoicePaymentMethodActivated : IHasInvoiceId
    {
        public PaymentMethodId PaymentMethodId { get; }
        public InvoiceEntity InvoiceEntity { get; }

        public InvoicePaymentMethodActivated(PaymentMethodId paymentMethodId, InvoiceEntity invoiceEntity)
        {
            PaymentMethodId = paymentMethodId;
            InvoiceEntity = invoiceEntity;
        }

        public string InvoiceId => InvoiceEntity.Id;

        public override string ToString()
        {
            return $"Invoice payment method activated for invoice {InvoiceId} ({PaymentMethodId})";
        }
    }
}
