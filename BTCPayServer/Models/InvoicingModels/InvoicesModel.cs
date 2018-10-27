using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoicesModel
    {
        public int Skip
        {
            get; set;
        }
        public int Count
        {
            get; set;
        }
        public string SearchTerm
        {
            get; set;
        }

        public List<InvoiceModel> Invoices
        {
            get; set;
        } = new List<InvoiceModel>();
        public string StatusMessage
        {
            get;
            set;
        }
    }

    public class InvoiceModel
    {
        public DateTimeOffset Date { get; set; }

        public string OrderId { get; set; }
        public string RedirectUrl { get; set; }
        public string InvoiceId
        {
            get; set;
        }

        public string Status
        {
            get; set;
        }
        public bool ShowCheckout { get; set; }
        public string ExceptionStatus { get; set; }
        public string AmountCurrency
        {
            get; set;
        }
        public string StatusMessage
        {
            get; set;
        }
    }
}
