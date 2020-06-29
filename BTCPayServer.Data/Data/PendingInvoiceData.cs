using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class PendingInvoiceData
    {
        public string Id
        {
            get; set;
        }
        public InvoiceData InvoiceData { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PendingInvoiceData>()
                .HasOne(o => o.InvoiceData)
                .WithMany(o => o.PendingInvoices)
                .HasForeignKey(o => o.Id).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
