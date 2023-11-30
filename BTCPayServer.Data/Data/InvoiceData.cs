using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class InvoiceData : IHasBlobUntyped
    {
        public string Id { get; set; }

        public string StoreDataId { get; set; }
        public StoreData StoreData { get; set; }

        public DateTimeOffset Created { get; set; }
        public List<PaymentData> Payments { get; set; }
        public List<InvoiceEventData> Events { get; set; }

        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public string ItemCode { get; set; }
        public string OrderId { get; set; }
        public string Status { get; set; }
        public string ExceptionStatus { get; set; }
        public string CustomerEmail { get; set; }
        public List<AddressInvoiceData> AddressInvoices { get; set; }
        public bool Archived { get; set; }
        public List<PendingInvoiceData> PendingInvoices { get; set; }
        public List<InvoiceSearchData> InvoiceSearchData { get; set; }
        public List<RefundData> Refunds { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<InvoiceData>()
                .HasOne(o => o.StoreData)
                .WithMany(a => a.Invoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceData>().HasIndex(o => o.StoreDataId);
            builder.Entity<InvoiceData>().HasIndex(o => o.OrderId);
            builder.Entity<InvoiceData>().HasIndex(o => o.Created);

            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<InvoiceData>()
                        .Property(o => o.Blob2)
                        .HasColumnType("JSONB");
            }
        }
    }
}
