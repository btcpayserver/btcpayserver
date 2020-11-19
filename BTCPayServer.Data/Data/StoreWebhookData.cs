using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace BTCPayServer.Data
{
    public class StoreWebhookData
    {
        public string StoreId { get; set; }
        public string WebhookId { get; set; }
        public WebhookData Webhook { get; set; }
        public StoreData Store { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<StoreWebhookData>()
                .HasKey(p => new { p.StoreId, p.WebhookId });

            builder.Entity<StoreWebhookData>()
                .HasOne(o => o.Webhook)
                .WithOne().OnDelete(DeleteBehavior.Cascade);

            builder.Entity<StoreWebhookData>()
                .HasOne(o => o.Store)
                .WithOne().OnDelete(DeleteBehavior.Cascade);
        }
    }
}
