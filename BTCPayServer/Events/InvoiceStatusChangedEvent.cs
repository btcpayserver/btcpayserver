using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceStatusChangedEvent
    {
        public InvoiceStatusChangedEvent()
        {

        }
        public InvoiceStatusChangedEvent(InvoiceEntity invoice, string newState)
        {
            OldState = invoice.Status;
            InvoiceId = invoice.Id;
            NewState = newState;
        }
        public string InvoiceId { get; set; }
        public string OldState { get; set; }
        public string NewState { get; set; }

        public override string ToString()
        {
            return $"Invoice {InvoiceId} changed status: {OldState} => {NewState}";
        }
    }
}
