using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoicesModel : BasePagingViewModel
    {
        public List<InvoiceModel> Invoices { get; set; } = new List<InvoiceModel>();
        public override int CurrentPageCount => Invoices.Count;
        public string[] StoreIds { get; set; }
        public string StoreId { get; set; }
        public bool IncludeArchived { get; set; }
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
        public string ExceptionStatus { get; set; }
        public string AmountCurrency { get; set; }

        public InvoiceDetailsModel Details { get; set; }
        public bool HasRefund { get; set; }
    }
}
