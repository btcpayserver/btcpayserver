using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Data
{
    public class PaymentRequestData
    {
        public string Id { get; set; }
        public DateTimeOffset Created
        {
            get; set;
        }
        public string StoreDataId { get; set; }

        public StoreData StoreData { get; set; }

        public PaymentRequestStatus Status { get; set; }

        public byte[] Blob { get; set; }

        public class PaymentRequestBlob
        {
            public decimal Amount { get; set; }
            public string Currency { get; set; }

            public DateTime? ExpiryDate { get; set; }

            public string Title { get; set; }
            public string Description { get; set; }
            public string Email { get; set; }

            public string EmbeddedCSS { get; set; }
            public string CustomCSSLink { get; set; }
            public bool AllowCustomPaymentAmounts { get; set; }
        }

        public enum PaymentRequestStatus
        {
            Pending = 0,
            Completed = 1,
            Expired = 2
        }
    }
}
