#nullable enable
using BTCPayServer.Payments;

namespace BTCPayServer.Events
{
    public class InvoiceNewPaymentDetailsEvent
    {

        public InvoiceNewPaymentDetailsEvent(string invoiceId, object? details, PaymentMethodId paymentMethodId)
        {
            InvoiceId = invoiceId;
            Details = details;
            PaymentMethodId = paymentMethodId;
        }
        public string InvoiceId { get; set; }
        public object? Details { get; }
        public PaymentMethodId PaymentMethodId { get; }
        public override string ToString()
        {
            return $"{PaymentMethodId.ToString()}: New payment details {Details?.GetType().Name} for invoice {InvoiceId}";
        }
    }
}
