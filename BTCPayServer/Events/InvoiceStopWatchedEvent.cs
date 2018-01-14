using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceStopWatchedEvent
    {
        public string InvoiceId { get; set; }
        public override string ToString()
        {
            return $"Invoice {InvoiceId} is not monitored anymore.";
        }
    }
}
