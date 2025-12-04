#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static BTCPayServer.Data.Subscriptions.SubscriberData;

namespace BTCPayServer.Data.Subscriptions;


[Table("subs_plans")]
public class PlanData : BaseEntityData
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    public List<SubscriberData> Subscriptions { get; set; } = null!;

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
    public PlanStatus Status { get; set; } = PlanStatus.Active;

    [Required]
    [Column("price")]
    public decimal Price { get; set; }

    [Required]
    [Column("currency")]
    public string Currency { get; set; } = null!;

    [Required]
    [Column("recurring_type")]
    public RecurringInterval RecurringType { get; set; } = RecurringInterval.Monthly;

    [Required]
    [Column("grace_period_days")]
    public int GracePeriodDays { get; set; }
    [Required]
    [Column("trial_days")]
    public int TrialDays { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("members_count")]
    public int MemberCount { get; set; }

    [Column("monthly_revenue")]
    public decimal MonthlyRevenue { get; set; }

    [Column("optimistic_activation")]
    public bool OptimisticActivation { get; set; }

    [Column("renewable")]
    public bool Renewable { get; set; } = true;

    public List<PlanChangeData> PlanChanges { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("plan"));
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.OptimisticActivation).HasDefaultValue(false);
        b.Property(x => x.RecurringType).HasConversion<string>();
        b.Property(x => x.Renewable).HasDefaultValue(true);
        b.HasOne(x => x.Offering).WithMany(x => x.Plans).HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
    }
    public enum PlanStatus
    {
        Active,
        Retired
    }

    public enum RecurringInterval
    {
        Monthly,
        Quarterly,
        Yearly,
        Lifetime
    }

    public (DateTimeOffset? PeriodEnd, DateTimeOffset? PeriodGraceEnd) GetPeriodEnd(DateTimeOffset from)
    {
        if (this.RecurringType == RecurringInterval.Lifetime)
            return (null, null);
        var to = from.AddMonths(this.RecurringType switch
            {
                RecurringInterval.Monthly => 1,
                RecurringInterval.Quarterly => 3,
                RecurringInterval.Yearly => 12,
                _ => throw new NotSupportedException(RecurringType.ToString())
            });
        return (to, GracePeriodDays is 0 ? null : to.AddDays(GracePeriodDays));
    }

    // Avoid cartesian explosion if there are lots of features
    private List<PlanFeatureData>? _planFeatures;
    [NotMapped]
    public List<PlanFeatureData> PlanFeatures
    {
        get => _planFeatures ?? throw FeatureNotLoadedException();
        set => _planFeatures = value;
    }

    private static InvalidOperationException FeatureNotLoadedException()
    {
        return new InvalidOperationException("PlanFeatures not loaded. Use ctx.PlanFeatures.EnsureFeatureLoaded() to load it");
    }
    [NotMapped]
    public bool FeaturesLoaded => _planFeatures is not null;

    public Task EnsureFeatureLoaded(ApplicationDbContext ctx) => EnsureFeatureLoaded(ctx.Plans);
    public async Task EnsureFeatureLoaded(DbSet<PlanData> set)
    {
        if (!FeaturesLoaded)
            await set.FetchPlanFeaturesAsync(this);
    }
    public Task ReloadFeature(ApplicationDbContext ctx) => ReloadFeature(ctx.Plans);
    public Task ReloadFeature(DbSet<PlanData> set) =>set.FetchPlanFeaturesAsync(this);

    public void AssertFeaturesLoaded() => _ = _planFeatures ?? throw FeatureNotLoadedException();

    public PlanFeatureData? GetFeature(long featureId)
        => PlanFeatures.FirstOrDefault(p => p.FeatureId == featureId);
    public PlanFeatureData? GetFeature(string featureCustomId)
        => PlanFeatures.FirstOrDefault(p => p.Feature.CustomId == featureCustomId);
    public string[] GetFeatureIds()
        => PlanFeatures.Select(p => p.Feature.CustomId).ToArray();

}
