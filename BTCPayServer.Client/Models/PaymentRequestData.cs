using System;

namespace BTCPayServer.Client.Models
{
    public class PaymentRequestData : PaymentRequestBaseData
    {
        public PaymentRequestData.PaymentRequestStatus Status { get; set; }
        public DateTimeOffset Created { get; set; }
        public string Id { get; set; }
        public bool Archived { get; set; }

        public enum PaymentRequestStatus
        {
            Pending = 0,
            Completed = 1,
            Expired = 2
        }
    }
}