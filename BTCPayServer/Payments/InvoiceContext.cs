#nullable enable
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    public class InvoiceContext
    {
        public InvoiceContext(InvoiceEntity entity)
        {
            Logs = new InvoiceLogs();
            Invoice = entity;
        }
        public InvoiceLogs Logs { get; }
        public InvoiceEntity Invoice { get; }
    }
}
