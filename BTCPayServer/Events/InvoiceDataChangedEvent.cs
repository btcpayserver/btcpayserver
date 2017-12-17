using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceDataChangedEvent
    {
        public string InvoiceId { get; set; }

        public override string ToString()
        {
            return $"Invoice {InvoiceId} data changed";
        }
    }
}
