using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
    public class PaymentModel
    {
        public string ServerUrl { get; set; }
        public string InvoiceId { get; set; }
        public string BtcAddress { get; set; }
        public string BtcDue { get; set; }
        public string CustomerEmail { get; set; }
        public int ExpirationSeconds { get; set; }
        public string Status { get; set; }
        public string MerchantRefLink { get; set; }
        public int MaxTimeSeconds { get; set; }

        // These properties are not used in client side code
        public string StoreName { get; set; }
        public string ItemDesc { get; set; }
        public string TimeLeft { get; set; }
        public string Rate { get; set; }
        public string BtcAmount { get; set; }
        public string TxFees { get; set; }
        public string InvoiceBitcoinUrl { get; set; }
        public string BtcTotalDue { get; set; }
        public int TxCount { get; set; }
        public string BtcPaid { get; set; }
        public string StoreEmail { get; set; }

        public string OrderId { get; set; }
    }
}
