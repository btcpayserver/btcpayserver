using System;
using System.Collections.Generic;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoicesModel : BasePagingViewModel
    {
        public List<InvoiceModel> Invoices { get; set; } = new ();
        public override int CurrentPageCount => Invoices.Count;
        public string StoreId { get; set; }
        public string SearchText { get; set; }
        public SearchString Search { get; set; }
        public List<InvoiceAppModel> Apps { get; set; }
    }

    public class InvoiceModel
    {
        public DateTimeOffset Date { get; set; }

        public string OrderId { get; set; }
        public string RedirectUrl { get; set; }
        public string InvoiceId { get; set; }
        public InvoiceState Status { get; set; }
        public bool CanMarkSettled { get; set; }
        public bool CanMarkInvalid { get; set; }
        public bool CanMarkStatus => CanMarkSettled || CanMarkInvalid;
        public bool ShowCheckout { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public InvoiceDetailsModel Details { get; set; }
        public bool HasRefund { get; set; }
    }
    
    public class InvoiceAppModel
    {
        public string Id { get; set; }
        public string AppName { get; set; }
        public string AppType { get; set; }
    }
}
