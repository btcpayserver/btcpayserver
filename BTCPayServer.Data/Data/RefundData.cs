using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace BTCPayServer.Data
{
    public class RefundData
    {
        [Required]
        public string InvoiceDataId { get; set; }
        [Required]
        [MaxLength(30)]
        public string PullPaymentDataId { get; set; }
        public PullPaymentData PullPaymentData { get; set; }
        public InvoiceData InvoiceData { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<RefundData>()
                .HasKey(nameof(InvoiceDataId), nameof(PullPaymentDataId));
            builder.Entity<RefundData>()
                .HasOne(o => o.InvoiceData)
                .WithMany(o => o.Refunds)
                .HasForeignKey(o => o.InvoiceDataId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
