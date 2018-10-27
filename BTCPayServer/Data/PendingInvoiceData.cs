using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class PendingInvoiceData
    {
        public string Id
        {
            get; set;
        }
        public InvoiceData InvoiceData { get; set; }
    }
}
