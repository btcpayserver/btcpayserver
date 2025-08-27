#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_subscriber_credits")]
public class SubscriberCredit
{
    [Required]
    [Column("subscriber_id")]
    public long SubscriberId { get; set; }

    [Required]
    [Column("currency")]
    public string Currency { get; set; } = null!;
    [Required]
    [Column("amount")]
    public decimal Amount { get; set; }

    [ForeignKey(nameof(SubscriberId))]
    public SubscriberData Subscriber { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriberCredit>();
        b.HasOne(x => x.Subscriber).WithMany(x => x.Credits).HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
        b.HasKey(x => new { x.SubscriberId, x.Currency });

        // Make sure currency is always uppercase at the db level
        b.Property(x => x.Currency).HasConversion(
            v => v.ToUpperInvariant(),
            v => v);

    }
}
