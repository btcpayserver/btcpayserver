using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Microsoft.EntityFrameworkCore;

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
        public DateTimeOffset Timestamp
        {
            get; set;
        }

        [Required]
        public byte[] Blob { get; set; }
        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<WebhookDeliveryData>()
                .HasOne(o => o.Webhook)
                .WithMany(a => a.Deliveries).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<WebhookDeliveryData>().HasIndex(o => o.WebhookId);
        }
    }
}
