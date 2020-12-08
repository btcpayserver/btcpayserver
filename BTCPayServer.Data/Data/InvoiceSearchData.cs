using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class InvoiceSearchData
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }
        [ForeignKey(nameof(InvoiceData))]
        public string InvoiceDataId { get; set; }
        public InvoiceData InvoiceData { get; set; }
        public string Value { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            
            builder.Entity<InvoiceSearchData>()
                .HasOne(o => o.InvoiceData)
                .WithMany(a => a.InvoiceSearchData)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InvoiceSearchData>().HasIndex(data => new {data.InvoiceDataId, data.Value});
            builder.Entity<InvoiceSearchData>().HasIndex(data => data.Value);
        }
    }
}
