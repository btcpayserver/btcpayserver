using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Data
{
    public static class InvoiceDataExtensions
    {
        public static InvoiceState GetInvoiceState(this InvoiceData invoiceData)
        {
            return new InvoiceState(invoiceData.Status, invoiceData.ExceptionStatus);
        }
    }
}
