using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class WebhookDeliveryData
    {
        [Key]
        [MaxLength(25)]
        public string Id { get; set; }
        [MaxLength(25)]
        [Required]
        public string WebhookId { get; set; }
        public WebhookData Webhook { get; set; }

        [Required]
        public DateTimeOffset Timestamp { get; set; }
        public string Blob { get; set; }
        public bool Pruned { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WebhookDeliveryData>()
                .HasOne(o => o.Webhook)
                .WithMany(a => a.Deliveries).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<WebhookDeliveryData>().HasIndex(o => o.WebhookId);
            builder.Entity<WebhookDeliveryData>().HasIndex(o => o.Timestamp);
            builder.Entity<WebhookDeliveryData>()
                .Property(o => o.Blob)
                .HasColumnType("JSONB");
        }
    }
}
