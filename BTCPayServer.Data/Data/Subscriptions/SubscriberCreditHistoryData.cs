#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_subscriber_credits_history")]
public class SubscriberCreditHistoryData
{
    [Key]
    public long Id { get; set; }

    [Column("subscriber_id")]
    public long SubscriberId { get; set; }

    [Required]
    [Column("currency")]
    public string Currency { get; set; } = null!;

    [Column("created_at", TypeName = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("description")]
    public string Description { get; set; } = null!;

    [Column("debit")]
    public decimal Debit { get; set; }

    [Column("credit")]
    public decimal Credit { get; set; }

    [Column("balance")]
    public decimal Balance { get; set; }

    public SubscriberCredit SubscriberCredit { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriberCreditHistoryData>();
        b.Property(o => o.Id).UseIdentityAlwaysColumn();
        b.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
        b.HasOne(o => o.SubscriberCredit).WithMany()
            .HasForeignKey(o => new { o.SubscriberId, o.Currency })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(o => new { o.SubscriberId, CreatedDate = o.CreatedAt }).IsDescending();
    }
}
