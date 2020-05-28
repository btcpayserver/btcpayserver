using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class NotificationData
    {
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
        public string NotificationType { get; set; }
        public bool Seen { get; set; }
        public byte[] Blob { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<NotificationData>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(n => n.Notifications)
                .HasForeignKey(k => k.ApplicationUserId);
        }
    }
}
