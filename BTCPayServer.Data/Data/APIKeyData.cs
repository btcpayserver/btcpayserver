using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class APIKeyData
    {
        [MaxLength(50)]
        public string Id
        {
            get;
            set;
        }

        [MaxLength(50)] public string StoreId { get; set; }

        [MaxLength(50)] public string UserId { get; set; }

        public APIKeyType Type { get; set; } = APIKeyType.Legacy;

        public byte[] Blob { get; set; }
        public StoreData StoreData { get; set; }
        public ApplicationUser User { get; set; }
        public string Label { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<APIKeyData>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.APIKeys)
                   .HasForeignKey(i => i.StoreId).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<APIKeyData>()
                .HasOne(o => o.User)
                .WithMany(i => i.APIKeys)
                .HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<APIKeyData>()
                .HasIndex(o => o.StoreId);
        }
    }

    public class APIKeyBlob
    {
        public string[] Permissions { get; set; }

    }

    public enum APIKeyType
    {
        Legacy,
        Permanent
    }
}
