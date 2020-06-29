using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class PaymentData
    {
        public string Id
        {
            get; set;
        }

        public string InvoiceDataId
        {
            get; set;
        }
        public InvoiceData InvoiceData
        {
            get; set;
        }

        public byte[] Blob
        {
            get; set;
        }
        public bool Accounted
        {
            get; set;
        }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PaymentData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.Payments).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentData>()
                   .HasIndex(o => o.InvoiceDataId);
        }
    }
}
