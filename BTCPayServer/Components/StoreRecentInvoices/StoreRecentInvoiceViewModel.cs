using System;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoiceViewModel
{
    public string InvoiceId { get; set; }
    public string OrderId { get; set; }
    public string AmountCurrency { get; set; }
    public InvoiceState Status { get; set; }
    public DateTimeOffset Date { get; set; }
    public bool HasRefund { get; set; }
}
