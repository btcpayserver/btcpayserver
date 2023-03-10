using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class PaymentData : IHasBlobUntyped
    {
        public string Id { get; set; }
        public string InvoiceDataId { get; set; }
        public InvoiceData InvoiceData { get; set; }
        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public string Type { get; set; }
        public bool Accounted { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<PaymentData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.Payments).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentData>()
                   .HasIndex(o => o.InvoiceDataId);
            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<PaymentData>()
                    .Property(o => o.Blob2)
                    .HasColumnType("JSONB");
            }
        }
    }
}
