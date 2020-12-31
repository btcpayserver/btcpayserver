using System;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class PairingCodeData
    {
        public string Id { get; set; }
        [Obsolete("Unused")]
        public string Facade { get; set; }
        public string StoreDataId { get; set; }
        public DateTimeOffset Expiration { get; set; }

        public string Label { get; set; }
        public string SIN { get; set; }
        public DateTime DateCreated { get; set; }
        public string TokenValue { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PairingCodeData>()
                .HasKey(o => o.Id);
        }
    }
}
