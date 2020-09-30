using BTCPayServer.Payments;

namespace BTCPayServer.Events
{
    public class InvoiceNewPaymentDetailsEvent
    {

        public InvoiceNewPaymentDetailsEvent(string invoiceId, IPaymentMethodDetails details, PaymentMethodId paymentMethodId)
        {
            InvoiceId = invoiceId;
            Details = details;
            PaymentMethodId = paymentMethodId;
        }

        public string Address { get; set; }
        public string InvoiceId { get; set; }
        public IPaymentMethodDetails Details { get; }
        public PaymentMethodId PaymentMethodId { get; }
        public override string ToString()
        {
            return $"{PaymentMethodId.ToPrettyString()}: New payment details {Details.GetPaymentDestination()} for invoice {InvoiceId}";
        }
    }
}
