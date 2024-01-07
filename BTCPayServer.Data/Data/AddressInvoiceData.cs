using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace BTCPayServer.Data
{
    public class AddressInvoiceData
    {
        /// <summary>
        /// Some crypto currencies share same address prefix
        /// For not having exceptions thrown by two address on different network, we suffix by "#CRYPTOCODE" 
        /// </summary>
        [Obsolete("Use GetHash instead")]
        public string Address { get; set; }
        public InvoiceData InvoiceData { get; set; }
        public string InvoiceDataId { get; set; }
        public DateTimeOffset? CreatedTime { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<AddressInvoiceData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.AddressInvoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<AddressInvoiceData>()
#pragma warning disable CS0618
                .HasKey(o => o.Address);
#pragma warning restore CS0618
        }
    }
}
