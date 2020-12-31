using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace BTCPayServer.Data
{
    public class InvoiceSearchData
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Key]
        public int Id { get; set; }

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

            builder.Entity<InvoiceSearchData>()
                .HasIndex(data => data.Value);

            builder.Entity<InvoiceSearchData>()
                .Property(a => a.Id)
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("MySql:ValueGeneratedOnAdd", true)
                .HasAnnotation("Sqlite:Autoincrement", true);
        }
    }
}
