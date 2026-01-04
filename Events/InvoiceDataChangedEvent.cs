using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceDataChangedEvent : IHasInvoiceId
    {
        public InvoiceDataChangedEvent(InvoiceEntity invoice)
        {
            InvoiceId = invoice.Id;
            State = invoice.GetInvoiceState();
        }
        public string InvoiceId { get; }
        public InvoiceState State { get; }

        public override string ToString()
        {
            return $"Invoice status is {State}";
        }
    }
}
