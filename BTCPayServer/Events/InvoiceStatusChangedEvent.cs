using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceStatusChangedEvent
    {
        public string InvoiceId { get; set; }
        public string OldState { get; set; }
        public string NewState { get; set; }

        public override string ToString()
        {
            return $"Invoice {InvoiceId} changed status: {OldState} => {NewState}";
        }
    }
}
