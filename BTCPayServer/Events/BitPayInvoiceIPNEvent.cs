using System;

namespace BTCPayServer.Events
{
    public class BitPayInvoiceIPNEvent
    {
        public BitPayInvoiceIPNEvent(string invoiceId, int? eventCode, string name)
        {
            InvoiceId = invoiceId;
            EventCode = eventCode;
            Name = name;
            Timestamp= DateTimeOffset.UtcNow;;
        }

        public DateTimeOffset Timestamp { get; set; }

        public int? EventCode { get; set; }
        public string Name { get; set; }

        public string InvoiceId { get; set; }
        public string Error { get; set; }

        public override string ToString()
        {
            string ipnType = "IPN";
            if (EventCode.HasValue)
            {
                ipnType = $"IPN ({EventCode.Value} {Name})";
            }
            if (Error == null)
                return $"{ipnType} sent for invoice {InvoiceId}";
            return $"Error while sending {ipnType}: {Error}";
        }
    }
}
