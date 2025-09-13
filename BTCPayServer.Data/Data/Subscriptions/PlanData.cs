#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;


[Table("subscriptions_plans")]
public class PlanData : BaseEntityData
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    public List<SubscriberData> Subscriptions { get; set; } = null!;
    public List<PlanEntitlementData> PlanEntitlements { get; set; } = null!;

    [Required]
    [Column("offering_id")]
    public string OfferingId { get; set; } = null!;

    [ForeignKey(nameof(OfferingId))]
    public OfferingData Offering { get; set; } = null!;

    [Required]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public PlanStatus Status { get; set; } = PlanStatus.Draft;

    [Required]
    [Column("price")]
    public decimal Price { get; set; }

    [Required]
    [Column("currency")]
    public string Currency { get; set; } = string.Empty;

    [Required]
    [Column("recurring_type")]
    public RecurringInterval RecurringType { get; set; } = RecurringInterval.Monthly;

    [Required]
    [Column("grace_period_days")]
    public int GracePeriodDays { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("allow_upgrade")]
    public bool AllowUpgrade { get; set; }

    [Column("members_count")]
    public int MemberCount { get; set; }

    [Column("renewable")]
    public bool Renewable { get; set; } = true;

    [Column("optimistic_activation")]
    public bool OptimisticActivation { get; set; } = true;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("plan"));
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.Renewable).HasDefaultValue(true);
        b.Property(x => x.OptimisticActivation).HasDefaultValue(true);
        b.Property(x => x.RecurringType).HasConversion<string>();
        b.HasOne(x => x.Offering).WithMany(x => x.Plans).HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
    }
    public enum PlanStatus
    {
        Active,
        Inactive,
        Draft
    }

    public enum RecurringInterval
    {
        Monthly,
        Quarterly,
        Yearly
    }

    public (DateTimeOffset PeriodEnd, DateTimeOffset PeriodGraceEnd) GetNextPeriodEnd(DateTimeOffset from)
    {
        // TODO: If it is a new plan, lastPeriodEnd can be null. If that is the case, take current date.
        // Later, we probably want to start beginning of the month for new plans.
        var to = from.AddMonths(this.RecurringType switch
            {
                RecurringInterval.Monthly => 1,
                RecurringInterval.Quarterly => 3,
                RecurringInterval.Yearly => 12,
                _ => throw new NotSupportedException(RecurringType.ToString())
            });
        return (to, to.AddDays(GracePeriodDays));
    }
}
