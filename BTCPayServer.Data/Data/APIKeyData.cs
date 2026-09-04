using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class APIKeyData : IHasBlob<APIKeyBlob>
    {
        public const string IdPrefix = "akid";
        public DateTimeOffset? CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string Id { get; set; }
        public string Hash { get; set; }

        public string Key { get; set; }
        public string Prefix { get; set; }

        public string StoreId { get; set; }

        public string UserId { get; set; }

        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public StoreData StoreData { get; set; }
        public ApplicationUser User { get; set; }
        public string Label { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<APIKeyData>().Property(x => x.CreatedAt).HasColumnName("CreatedAt").HasColumnType("timestamptz")
                .HasDefaultValueSql("now()");
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

        public static bool IsId(string apiKeyId)
        => apiKeyId.StartsWith(APIKeyData.IdPrefix + "_", StringComparison.OrdinalIgnoreCase);
    }

    public class APIKeyBlob
    {
        public string[] Permissions { get; set; }
        public string ApplicationIdentifier { get; set; }
        public string ApplicationAuthority { get; set; }

    }
}
