using System;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class PaymentRequestData
    {
        public string Id { get; set; }
        public DateTimeOffset Created
        {
            get; set;
        }
        public string StoreDataId { get; set; }
        public bool Archived { get; set; }

        public StoreData StoreData { get; set; }

        public Client.Models.PaymentRequestData.PaymentRequestStatus Status { get; set; }

        public byte[] Blob { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
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
        }
    }
}
