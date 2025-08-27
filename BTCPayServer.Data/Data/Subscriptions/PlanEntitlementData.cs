#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_plans_entitlements")]
public class PlanEntitlementData
{
    [Required]
    [Column("plan_id")]
    public string PlanId { get; set; } = null!;

    [ForeignKey(nameof(PlanId))]
    public PlanData Plan { get; set; } = null!;

    [Required]
    [Column("entitlement_id")]
    public long EntitlementId { get; set; }

    [ForeignKey(nameof(EntitlementId))]
    public EntitlementData Entitlement { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanEntitlementData>();
        b.HasKey(o => new { o.PlanId, o.EntitlementId });
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Entitlement).WithMany().HasForeignKey(x => x.EntitlementId).OnDelete(DeleteBehavior.Cascade);
    }
}
