using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public partial class PaymentRequestData : IHasBlobUntyped
    {
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        public string StoreDataId { get; set; }
        public bool Archived { get; set; }

        public StoreData StoreData { get; set; }

        public Client.Models.PaymentRequestData.PaymentRequestStatus Status { get; set; }

        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<PaymentRequestData>()
                .HasOne(o => o.StoreData)
                .WithMany(i => i.PaymentRequests)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentRequestData>()
                .Property(e => e.Created)
                .HasDefaultValue(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero));
            builder.Entity<PaymentRequestData>()
                .HasIndex(o => o.Status);

            builder.Entity<PaymentRequestData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }
}
