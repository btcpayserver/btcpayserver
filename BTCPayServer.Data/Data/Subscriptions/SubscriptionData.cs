#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subscriptions_subs")]
public class SubscriptionData : BaseEntityData
{
    [Key]
    [Required]
    [Column("id")]
    public long Id { get; set; }


    [Required]
    [Column("offering_id")]
    public string OfferingId { get; set; } = null!;
    [ForeignKey(nameof(OfferingId))]
    public OfferingData Offering { get; set; } = null!;

    [Required]
    [Column("customer_id")]
    public string CustomerId { get; set; } = null!;

    [ForeignKey(nameof(CustomerId))]
    public CustomerData Customer { get; set; } = null!;

    [Required]
    [Column("plan_id")]
    public string PlanId { get; set; } = null!;

    [ForeignKey(nameof(PlanId))]
    public PlanData Plan { get; set; } = null!;

    public List<SubscriptionEntitlementData> MembershipEntitlements { get; set; } = null!;

    [Required]
    [Column("phase")]
    public PhaseTypes Phase { get; set; } = PhaseTypes.Expired;

    [Column("period_end")]
    public DateTimeOffset? PeriodEnd { get; set; }
    [Column("trial_end")]
    public DateTimeOffset? TrialEnd { get; set; }

    [Column("grace_period_end")]
    public DateTimeOffset? GracePeriodEnd { get; set; }

    [Column("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [Required]
    [Column("active")]
    public bool IsActive { get; set; } = false;

    [Required]
    [Column("force_disabled")]
    public bool ForceDisabled { get; set; } = false;

    public PhaseTypes CalculateExpectedPhase(DateTimeOffset now)
        => this switch
        {
            { TrialEnd: { } te } when now < te => PhaseTypes.Trial,
            { PeriodEnd: { } pe } when now < pe => PhaseTypes.Normal,
            { GracePeriodEnd: { } gpe } when now < gpe => PhaseTypes.Grace,
            _ => PhaseTypes.Expired
        };

    public enum PhaseTypes
    {
        Trial,
        Normal,
        Grace,
        Expired
    }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriptionData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.Phase)
            .HasSentinel(PhaseTypes.Expired)
            .HasDefaultValueSql("'Expired'::TEXT").HasConversion<string>();
        b.Property(x => x.IsActive).HasDefaultValue(false);
        b.Property(x => x.ForceDisabled).HasDefaultValue(false);
        b.HasIndex(c => new { c.OfferingId, c.CustomerId })
            .IsUnique();
        b.HasOne(x => x.Plan).WithMany(x => x.Subscriptions).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Offering).WithMany(x => x.Subscriptions).HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
    }
}
