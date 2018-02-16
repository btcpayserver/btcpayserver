using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceNeedUpdateEvent
    {
        public InvoiceNeedUpdateEvent(string invoiceId)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            InvoiceId = invoiceId;
        }

        public string InvoiceId { get; set; }

        public override string ToString()
        {
            return string.Empty;
        }
    }
}
