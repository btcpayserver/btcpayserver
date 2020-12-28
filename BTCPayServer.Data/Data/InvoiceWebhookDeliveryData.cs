using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class InvoiceWebhookDeliveryData
    {
        public string InvoiceId { get; set; }
        public InvoiceData Invoice { get; set; }
        public string DeliveryId { get; set; }
        public WebhookDeliveryData Delivery { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<InvoiceWebhookDeliveryData>()
                .HasKey(p => new { p.InvoiceId, p.DeliveryId });
            builder.Entity<InvoiceWebhookDeliveryData>()
                .HasOne(o => o.Invoice)
                .WithOne().OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceWebhookDeliveryData>()
                .HasOne(o => o.Delivery)
                .WithOne().OnDelete(DeleteBehavior.Cascade);
        }
    }
}
