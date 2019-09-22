using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class HistoricalAddressInvoiceData
    {
        public string InvoiceDataId
        {
            get; set;
        }

        public InvoiceData InvoiceData
        {
            get; set;
        }

        /// <summary>
        /// Some crypto currencies share same address prefix
        /// For not having exceptions thrown by two address on different network, we suffix by "#CRYPTOCODE" 
        /// </summary>
        [Obsolete("Use GetCryptoCode instead")]
        public string Address
        {
            get; set;
        }


        [Obsolete("Use GetCryptoCode instead")]
        public string CryptoCode { get; set; }

        public DateTimeOffset Assigned
        {
            get; set;
        }

        public DateTimeOffset? UnAssigned
        {
            get; set;
        }
    }
}
