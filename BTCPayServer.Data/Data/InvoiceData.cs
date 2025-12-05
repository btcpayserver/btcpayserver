using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json.Linq;


namespace BTCPayServer.Data
{
    public partial class InvoiceData : IHasBlobUntyped
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public decimal? Amount { get; set; }
        public string StoreDataId { get; set; }
        public StoreData StoreData { get; set; }

        public DateTimeOffset Created { get; set; }
        public List<PaymentData> Payments { get; set; }

        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public string Status { get; set; }
        public string ExceptionStatus { get; set; }
        public List<AddressInvoiceData> AddressInvoices { get; set; }
        public bool Archived { get; set; }
        public List<InvoiceSearchData> InvoiceSearchData { get; set; }
        public List<RefundData> Refunds { get; set; }

		public static string GetOrderId(string blob) => throw new NotSupportedException();
		public static string GetItemCode(string blob) => throw new NotSupportedException();
        public static bool IsPending(string status) => throw new NotSupportedException();

        [Timestamp]
        // With this, update of InvoiceData will fail if the row was modified by another process
        public uint XMin { get; set; }

        public const string Processing = nameof(Processing);
        public const string Settled = nameof(Settled);
        public const string Invalid = nameof(Invalid);
        public const string Expired = nameof(Expired);

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<InvoiceData>()
                .HasOne(o => o.StoreData)
                .WithMany(a => a.Invoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceData>().HasIndex(o => o.StoreDataId);
            builder.Entity<InvoiceData>().HasIndex(o => o.Created);
            builder.Entity<InvoiceData>()
                    .Property(o => o.Blob2)
                    .HasColumnType("JSONB");
            builder.Entity<InvoiceData>()
                    .Property(o => o.Amount)
                    .HasColumnType("NUMERIC");
			builder.HasDbFunction(typeof(InvoiceData).GetMethod(nameof(GetOrderId), new[] { typeof(string) }), b => b.HasName("get_orderid"));
			builder.HasDbFunction(typeof(InvoiceData).GetMethod(nameof(GetItemCode), new[] { typeof(string) }), b => b.HasName("get_itemcode"));
            builder.HasDbFunction(typeof(InvoiceData).GetMethod(nameof(IsPending), new[] { typeof(string) }), b => b.HasName("is_pending"));
        }
    }
}
