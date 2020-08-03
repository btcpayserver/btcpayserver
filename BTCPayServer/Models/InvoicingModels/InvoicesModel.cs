using System;
using System.Collections.Generic;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoicesModel : BasePagingViewModel
    {
        public List<InvoiceModel> Invoices { get; set; } = new List<InvoiceModel>();
        public string[] StoreIds { get; set; }
    }

    public class InvoiceModel
    {
        public DateTimeOffset Date { get; set; }

        public string OrderId { get; set; }
        public string RedirectUrl { get; set; }
        public string InvoiceId { get; set; }

        public InvoiceStatus Status { get; set; }
        public string StatusString { get; set; }
        public bool CanMarkComplete { get; set; }
        public bool CanMarkInvalid { get; set; }
        public bool CanMarkStatus => CanMarkComplete || CanMarkInvalid;
        public bool ShowCheckout { get; set; }
        public string ExceptionStatus { get; set; }
        public string AmountCurrency { get; set; }

        public InvoiceDetailsModel Details { get; set; }
    }
}
