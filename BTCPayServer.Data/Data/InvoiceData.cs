using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

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
        public bool Archived { get; set; }
        public List<PendingInvoiceData> PendingInvoices { get; set; }
        public List<RefundData> Refunds { get; set; }
        public string CurrentRefundId { get; set; }
        [ForeignKey("Id,CurrentRefundId")]
        public RefundData CurrentRefund { get; set; }
        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<InvoiceData>()
                .HasOne(o => o.StoreData)
                .WithMany(a => a.Invoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceData>().HasIndex(o => o.StoreDataId);
            builder.Entity<InvoiceData>()
                .HasOne(o => o.CurrentRefund);
        }
    }
}
