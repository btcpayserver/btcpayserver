using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class APIKeyData : IHasBlob<APIKeyBlob>
    {
        [MaxLength(50)]
        public string Id { get; set; }

        [MaxLength(50)]
        public string StoreId { get; set; }

        [MaxLength(50)]
        public string UserId { get; set; }

        public APIKeyType Type { get; set; } = APIKeyType.Legacy;

        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public StoreData StoreData { get; set; }
        public ApplicationUser User { get; set; }
        public string Label { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
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

            builder.Entity<APIKeyData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }

    public class APIKeyBlob
    {
        public string[] Permissions { get; set; }
        public string ApplicationIdentifier { get; set; }
        public string ApplicationAuthority { get; set; }

    }

    public enum APIKeyType
    {
        Legacy,
        Permanent
    }
}
