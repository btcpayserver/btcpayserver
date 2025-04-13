using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class NotificationData : IHasBlobUntyped
    {
        [MaxLength(36)]
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        [MaxLength(50)]
        [Required]
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
        [MaxLength(100)]
        [Required]
        public string NotificationType { get; set; }
        public bool Seen { get; set; }
        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<NotificationData>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(n => n.Notifications)
                .HasForeignKey(k => k.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<NotificationData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }
}
