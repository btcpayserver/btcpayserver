using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class WebhookData : IHasBlobUntyped
    {
        [Key]
        [MaxLength(25)]
        public string Id { get; set; }
        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public List<WebhookDeliveryData> Deliveries { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WebhookData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }
}
