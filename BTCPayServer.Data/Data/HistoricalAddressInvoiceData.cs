using System;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class HistoricalAddressInvoiceData
    {
        public string InvoiceDataId
        {
            get; set;
        }

        public InvoiceData InvoiceData
        {
            get; set;
        }

        /// <summary>
        /// Some crypto currencies share same address prefix
        /// For not having exceptions thrown by two address on different network, we suffix by "#CRYPTOCODE" 
        /// </summary>
        [Obsolete("Use GetCryptoCode instead")]
        public string Address
        {
            get; set;
        }


        [Obsolete("Use GetCryptoCode instead")]
        public string CryptoCode { get; set; }

        public DateTimeOffset Assigned
        {
            get; set;
        }

        public DateTimeOffset? UnAssigned
        {
            get; set;
        }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<HistoricalAddressInvoiceData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.HistoricalAddressInvoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<HistoricalAddressInvoiceData>()
                .HasKey(o => new
                {
                    o.InvoiceDataId,
#pragma warning disable CS0618
                    o.Address
#pragma warning restore CS0618
                });
        }
    }
}
