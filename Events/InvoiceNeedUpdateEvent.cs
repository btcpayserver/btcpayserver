using System;

namespace BTCPayServer.Events
{
    public class InvoiceNeedUpdateEvent
    {
        public InvoiceNeedUpdateEvent(string invoiceId)
        {
            ArgumentNullException.ThrowIfNull(invoiceId);
            InvoiceId = invoiceId;
        }

        public string InvoiceId { get; set; }

        public override string ToString()
        {
            return $"Invoice {InvoiceId} needs update";
        }
    }
}
