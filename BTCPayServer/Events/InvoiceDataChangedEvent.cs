using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoiceDataChangedEvent
    {
        public InvoiceDataChangedEvent(InvoiceEntity invoice)
        {
            InvoiceId = invoice.Id;
            Status = invoice.Status;
            ExceptionStatus = invoice.ExceptionStatus;
        }
        public string InvoiceId { get; set; }
        public string Status { get; internal set; }
        public string ExceptionStatus { get; internal set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(ExceptionStatus) || ExceptionStatus == "false")
            { 
                return $"Invoice status is {Status}";
            }
            else
            {
                return $"Invoice status is {Status} (Exception status: {ExceptionStatus})";
            }
        }
    }
}
