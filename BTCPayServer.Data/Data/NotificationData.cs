using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class NotificationData
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
        public byte[] Blob { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<NotificationData>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(n => n.Notifications)
                .HasForeignKey(k => k.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
