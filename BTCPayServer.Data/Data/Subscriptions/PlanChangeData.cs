using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_plan_changes")]
public class PlanChangeData
{
    [Required]
    [Column("plan_id")]
    public string PlanId { get; set; } = null!;
    [ForeignKey(nameof(PlanId))]
    public PlanData Plan { get; set; } = null!;

    [Required]
    [Column("plan_change_id")]
    public string PlanChangeId { get; set; } = null!;
    [ForeignKey(nameof(PlanChangeId))]
    public PlanData PlanChange { get; set; } = null!;

    [Required]
    [Column("type")]
    public ChangeType Type { get; set; }
    public enum ChangeType
    {
        Upgrade,
        Downgrade
    }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanChangeData>();
        b.HasKey(x => new { x.PlanId, x.PlanChangeId });
        b.Property(x => x.Type).HasConversion<string>();
        b.HasOne(o => o.Plan).WithMany(o => o.PlanChanges).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(o => o.PlanChange).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
