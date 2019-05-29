using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceEvent
    {
        public const string Created = "invoice_created";
        public const string ReceivedPayment = "invoice_receivedPayment";
        public const string MarkedCompleted = "invoice_markedComplete";
        public const string MarkedInvalid= "invoice_markedInvalid";
        public const string Expired= "invoice_expired";
        public const string ExpiredPaidPartial= "invoice_expiredPaidPartial";
        public const string PaidInFull= "invoice_paidInFull";
        public const string PaidAfterExpiration= "invoice_paidAfterExpiration";
        public const string FailedToConfirm= "invoice_failedToConfirm";
        public const string Confirmed= "invoice_confirmed";
        public const string Completed= "invoice_completed";
        
        public InvoiceEvent(InvoiceEntity invoice, int code, string name)
        {
            Invoice = invoice;
            EventCode = code;
            Name = name;
        }

        public InvoiceEntity Invoice { get; set; }
        public int EventCode { get; set; }
        public string Name { get; set; }

        public PaymentEntity Payment { get; set; }

        public override string ToString()
        {
            return $"Invoice {Invoice.Id} new event: {Name} ({EventCode})";
        }
    }
}
