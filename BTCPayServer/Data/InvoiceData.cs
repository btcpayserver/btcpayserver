using BTCPayServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class InvoiceData
    {
        public string StoreDataId
        {
            get; set;
        }
        public StoreData StoreData
        {
            get; set;
        }

        public string Id
        {
            get; set;
        }

        public DateTimeOffset Created
        {
            get; set;
        }

        public List<PaymentData> Payments
        {
            get; set;
        }

        public List<InvoiceEventData> Events
        {
            get; set;
        }

        public List<RefundAddressesData> RefundAddresses
        {
            get; set;
        }

        public List<HistoricalAddressInvoiceData> HistoricalAddressInvoices
        {
            get; set;
        }

        public byte[] Blob
        {
            get; set;
        }
        public string ItemCode
        {
            get;
            set;
        }
        public string OrderId
        {
            get;
            set;
        }
        public string Status
        {
            get;
            set;
        }
        public string ExceptionStatus
        {
            get;
            set;
        }
        public string CustomerEmail
        {
            get;
            set;
        }
        public List<AddressInvoiceData> AddressInvoices
        {
            get; set;
        }
        public List<PendingInvoiceData> PendingInvoices { get; set; }
    }
}
