using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        public string UniqueId { get; internal set; }
        public DateTimeOffset Timestamp
        {
            get; set;
        }

        public string Message { get; set; }
    }
}
