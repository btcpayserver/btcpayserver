using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
    public class PaymentModel
    {
        public class AvailableCrypto
        {
            public string PaymentMethodId { get; set; }
            public string CryptoImage { get; set; }
            public string Link { get; set; }
        }
        public List<AvailableCrypto> AvailableCryptos { get; set; } = new List<AvailableCrypto>();
        public string CryptoCode { get; set; }
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
        public string OrderAmount { get; set; }
        public string InvoiceBitcoinUrl { get; set; }
        public string InvoiceBitcoinUrlQR { get; set; }
        public int TxCount { get; set; }
        public string BtcPaid { get; set; }
        public string StoreEmail { get; set; }

        public string OrderId { get; set; }
        public string CryptoImage { get; set; }
        public string NetworkFeeDescription { get; internal set; }
        public int MaxTimeMinutes { get; internal set; }
        public string PaymentType { get; internal set; }
        public string PaymentMethodId { get; internal set; }

        public bool AllowCoinConversion { get; set; }
    }
}
