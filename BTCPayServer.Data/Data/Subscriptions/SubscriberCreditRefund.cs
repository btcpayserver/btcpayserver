using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Data.Subscriptions;

[Table("subs_credit_refunds")]
public class SubscriberCreditRefund
{
    [Key]
    [Column("pull_payment_id")]
    public string PullPaymentId { get; set; }

    [Column("subscriber_id")]
    public long SubscriberId { get; set; }

    [ForeignKey(nameof(SubscriberId))]
    public SubscriberData Subscriber { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("currency")]
    public string Currency { get; set; }

    [Column("deducted")]
    public bool Deducted { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriberCreditRefund>();
        b.HasKey(x => x.PullPaymentId);
        b.Property(x => x.Deducted).HasDefaultValue(false);
        b.HasOne(x => x.Subscriber).WithMany()
            .HasForeignKey(x => x.SubscriberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
