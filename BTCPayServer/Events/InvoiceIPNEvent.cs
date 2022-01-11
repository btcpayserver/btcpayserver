namespace BTCPayServer.Events
{
    public class InvoiceIPNEvent : IHasInvoiceId
    {
        public InvoiceIPNEvent(string invoiceId, int? eventCode, string name, bool extendedNotification)
        {
            InvoiceId = invoiceId;
            EventCode = eventCode;
            Name = name;
            ExtendedNotification = extendedNotification;
        }

        public int? EventCode { get; set; }
        public string Name { get; set; }
        public bool ExtendedNotification { get; }
        public string InvoiceId { get; set; }
        public string Error { get; set; }

        public override string ToString()
        {
            string ipnType = "IPN";
            if (EventCode.HasValue)
            {
                string suffix = string.Empty;
                if (ExtendedNotification)
                    suffix = " ExtendedNotification";
                ipnType = $"IPN ({EventCode.Value} {Name}{suffix})";
            }
            else
            {
                string suffix = string.Empty;
                if (ExtendedNotification)
                    suffix = " (ExtendedNotification)";
                ipnType += suffix;
            }
            if (Error == null)
                return $"{ipnType} sent for invoice {InvoiceId}";
            return $"Error while sending {ipnType}: {Error}";
        }
    }
}
