using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceDataChangedEvent
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
