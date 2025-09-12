using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subscriptions_subs_entitlements")]
public class SubscriptionEntitlementData
{
    [Required]
    [Column("subs_id")]
    public long SubscriptionId { get; set; }

    [ForeignKey(nameof(SubscriptionId))]
    public SubscriptionData Subscription { get; set; } = null!;

    [Required]
    [Column("entitlement_id")]
    public long EntitlementId { get; set; }

    [ForeignKey(nameof(EntitlementId))]
    public EntitlementData Entitlement { get; set; } = null!;

    [Column("quantity")]
    public decimal Quantity { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriptionEntitlementData>();
        b.HasKey(o => new { o.SubscriptionId, o.EntitlementId });
        b.HasOne(x => x.Subscription).WithMany(x => x.MembershipEntitlements).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Cascade);
    }
}
