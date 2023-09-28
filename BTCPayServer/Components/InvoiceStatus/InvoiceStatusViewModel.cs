using System.Collections.Generic;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Components.InvoiceStatus
{
    public class InvoiceStatusViewModel
    {
        public InvoiceState State { get; set; }
        public List<PaymentEntity> Payments { get; set; }
        public string InvoiceId { get; set; }
        public bool IsArchived { get; set; }
        public bool HasRefund { get; set; }
    }
}
