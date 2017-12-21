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

        public string Address
        {
            get; set;
        }


        [Obsolete("Use GetCryptoCode instead")]
        public string CryptoCode { get; set; }

#pragma warning disable CS0618
        public string GetCryptoCode()
        {
            return string.IsNullOrEmpty(CryptoCode) ? "BTC" : CryptoCode;
        }
#pragma warning restore CS0618

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
