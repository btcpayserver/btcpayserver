using System;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class PairedSINData
    {
        public string Id { get; set; }

        public string StoreDataId { get; set; }

        public StoreData StoreData { get; set; }

        public string Label { get; set; }
        public DateTimeOffset PairingTime { get; set; }
        public string SIN { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PairedSINData>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.PairedSINs).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PairedSINData>(b =>
            {
                b.HasIndex(o => o.SIN);
                b.HasIndex(o => o.StoreDataId);
            });
        }
    }
}
