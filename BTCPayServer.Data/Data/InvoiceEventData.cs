using System;

namespace BTCPayServer.Data
{
    public class InvoiceEventData
    {
        public string InvoiceDataId
        {
            get; set;
        }
        public InvoiceData InvoiceData
        {
            get; set;
        }
        public string UniqueId { get; set; }
        public DateTimeOffset Timestamp
        {
            get; set;
        }

        public string Message { get; set; }
    }
}
