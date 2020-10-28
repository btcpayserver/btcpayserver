using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceDataChangedEvent
    {

        public InvoiceDataChangedEvent(InvoiceEntity invoice)
        {
            Invoice = invoice;
        }

        public readonly InvoiceEntity Invoice;
        public string InvoiceId => Invoice.Id;
        public InvoiceState State  => Invoice.GetInvoiceState();

        public override string ToString()
        {
            return $"Invoice status is {State}";
        }
    }
}
