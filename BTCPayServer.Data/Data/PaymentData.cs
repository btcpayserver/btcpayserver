using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public enum PaymentStatus
    {
        Processing,
        Settled,
        Unaccounted
    }
    public partial class PaymentData : IHasBlobUntyped
    {
        /// <summary>
        /// The date of creation of the payment
        /// Note that while it is a nullable field, our migration
        /// process ensure it is populated.
        /// </summary>
        public DateTimeOffset? Created { get; set; }
        public string Id { get; set; }
        public string InvoiceDataId { get; set; }
        public string Currency { get; set; }
        public decimal? Amount { get; set; }
        public InvoiceData InvoiceData { get; set; }
        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public string PaymentMethodId { get; set; }
        [Obsolete("Use Status instead")]
        public bool? Accounted { get; set; }
        public PaymentStatus? Status { get; set; }
        public static bool IsPending(PaymentStatus? status) => throw new NotSupportedException();
        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PaymentData>()
                .HasKey(o => new { o.Id, o.PaymentMethodId });
            builder.Entity<PaymentData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.Payments).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentData>()
                   .HasIndex(o => o.InvoiceDataId);
            builder.Entity<PaymentData>()
                   .Property(o => o.Status)
                   .HasConversion<string>();
            builder.Entity<PaymentData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
            builder.Entity<PaymentData>()
                    .Property(o => o.Amount)
                    .HasColumnType("NUMERIC");
            builder.HasDbFunction(typeof(PaymentData).GetMethod(nameof(IsPending), new[] { typeof(PaymentStatus?) }), b => b.HasName("is_pending"));
        }
    }
}
