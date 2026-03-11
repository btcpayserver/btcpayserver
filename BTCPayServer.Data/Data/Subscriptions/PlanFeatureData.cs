#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_plans_features")]
public class PlanFeatureData
{
    [Required]
    [Column("plan_id")]
    public string PlanId { get; set; } = null!;

    [ForeignKey(nameof(PlanId))]
    public PlanData Plan { get; set; } = null!;

    [Required]
    [Column("feature_id")]
    public long FeatureId { get; set; }

    [ForeignKey(nameof(FeatureId))]
    public FeatureData Feature { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanFeatureData>();
        b.HasKey(o => new { o.PlanId, o.FeatureId });
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Feature).WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
    }
}
