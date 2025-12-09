using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Abstractions;
using BTCPayServer.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_portal_sessions")]
public class PortalSessionData
{
    [Required]
    [Key]
    [Column("id")]
    public string Id { get; set; }
    [Column("subscriber_id")]
    public long SubscriberId { get; set; }

    [ForeignKey(nameof(SubscriberId))]
    public SubscriberData Subscriber { get; set; }

    public StoreData GetStoreData() => Subscriber?.Offering?.App?.StoreData ?? throw new InvalidOperationException("You need to include the store in the query");

    [Required]
    [Column("expiration")]
    public DateTimeOffset Expiration { get; set; }

    [Required]
    [Column("base_url", TypeName = "text")]
    public RequestBaseUrl BaseUrl { get; set; }

    [NotMapped]
    public bool IsExpired => DateTimeOffset.UtcNow > Expiration;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PortalSessionData>();
        b.HasOne(x => x.Subscriber).WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("ps"));
        b.HasIndex(x => x.Expiration);
        b.Property(x => x.Expiration).HasDefaultValueSql("now() + interval '1 day'");
        b.Property(x => x.BaseUrl)
            .HasConversion<string>(
                x => x.ToString(),
                x => RequestBaseUrl.FromUrl(x)
            );

    }
}
