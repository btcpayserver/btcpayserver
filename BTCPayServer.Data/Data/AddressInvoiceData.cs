using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class AddressInvoiceData
    {
        /// <summary>
        /// Some crypto currencies share same address prefix
        /// For not having exceptions thrown by two address on different network, we suffix by "#CRYPTOCODE" 
        /// </summary>
        [Obsolete("Use GetHash instead")]
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
