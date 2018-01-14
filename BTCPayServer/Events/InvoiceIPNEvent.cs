using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceIPNEvent
    {
        public InvoiceIPNEvent(string invoiceId)
        {
            InvoiceId = invoiceId;
        }

        public string InvoiceId { get; set; }
        public string Error { get; set; }

        public override string ToString()
        {
            if (Error == null)
                return $"IPN sent for invoice {InvoiceId}";
            return $"Error while sending IPN: {Error}";
        }
    }
}
