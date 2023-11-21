using System;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoiceViewModel
{
    public string InvoiceId { get; set; }
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public InvoiceState Status { get; set; }
    public DateTimeOffset Date { get; set; }
    public InvoiceDetailsModel Details { get; set; }
    public bool HasRefund { get; set; }
}
