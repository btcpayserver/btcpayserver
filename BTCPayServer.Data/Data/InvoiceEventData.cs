using System;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class InvoiceEventData
    {
        public string InvoiceDataId
        {
            get; set;
        }
        public InvoiceData InvoiceData
        {
            get; set;
        }
        public string UniqueId { get; set; }
        public DateTimeOffset Timestamp
        {
            get; set;
        }

        public string Message { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<InvoiceEventData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.Events).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceEventData>()
                .HasKey(o => new
                {
                    o.InvoiceDataId,
#pragma warning disable CS0618
                    o.UniqueId
#pragma warning restore CS0618
                });
        }
    }
}
