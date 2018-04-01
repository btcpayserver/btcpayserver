using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceEvent
    {
        public InvoiceEvent(InvoiceEntity invoice, int code, string name) : this(invoice.Id, code, name)
        {

        }
        public InvoiceEvent(string invoiceId, int code, string name)
        {
            InvoiceId = invoiceId;
            EventCode = code;
            Name = name;
        }

        public string InvoiceId { get; set; }
        public int EventCode { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return $"Invoice {InvoiceId} new event: {Name} ({EventCode})";
        }
    }
}
