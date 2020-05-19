using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.Client.Models;

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
        public bool Archived { get; set; }

        public StoreData StoreData { get; set; }

        public Client.Models.PaymentRequestData.PaymentRequestStatus Status { get; set; }

        public byte[] Blob { get; set; }
        
    }
}
