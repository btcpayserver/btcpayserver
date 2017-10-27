using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class AddressInvoiceData
    {
        public string Address
        {
            get; set;
        }

        public InvoiceData InvoiceData
        {
            get; set;
        }

        public string InvoiceDataId
        {
            get; set;
        }

        public DateTimeOffset? CreatedTime
        {
            get; set;
        }
    }
}
