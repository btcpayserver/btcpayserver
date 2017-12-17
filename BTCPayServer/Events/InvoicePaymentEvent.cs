using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoicePaymentEvent
    {
        
        public InvoicePaymentEvent(string invoiceId)
        {
            InvoiceId = invoiceId;
        }

        public string InvoiceId { get; set; }

        public override string ToString()
        {
            return $"Invoice {InvoiceId} received a payment";
        }
    }
}
