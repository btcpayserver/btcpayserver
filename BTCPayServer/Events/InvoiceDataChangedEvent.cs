using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceDataChangedEvent
    {
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
