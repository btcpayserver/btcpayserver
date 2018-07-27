using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceEvent
    {
        public InvoiceEvent(Models.InvoiceResponse invoice, int code, string name)
        {
            Invoice = invoice;
            EventCode = code;
            Name = name;
        }

        public Models.InvoiceResponse Invoice { get; set; }
        public int EventCode { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return $"Invoice {Invoice.Id} new event: {Name} ({EventCode})";
        }
    }
}
