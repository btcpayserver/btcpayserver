#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_subscribers")]
public class SubscriberData : BaseEntityData
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

    [NotMapped]
    public PlanData NextPlan => NewPlan ?? Plan;

    [Column("new_plan_id")]
    public string? NewPlanId { get; set; }

    [ForeignKey(nameof(NewPlanId))]
    public PlanData? NewPlan { get; set; }

    [ForeignKey(nameof(PlanId))]
    public PlanData Plan { get; set; } = null!;

    [Column("paid_amount")]
    public decimal? PaidAmount { get; set; }

    public decimal GetCredit(string? currency = null)
        => Credits.FirstOrDefault(c => (currency ?? c.Currency).Equals(Plan.Currency, StringComparison.OrdinalIgnoreCase))?.Amount ?? 0m;

    public decimal MissingCredit()
    => Math.Max(0m, NextPlan.Price - GetCredit(NextPlan.Currency));

    [Column("processing_invoice_id")]
    public string? ProcessingInvoiceId { get; set; }

    [Required]
    [Column("phase")]
    public PhaseTypes Phase { get; set; } = PhaseTypes.Expired;

    [Column("plan_started")]
    public DateTimeOffset PlanStarted { get; set; }

    [Column("period_end")]
    public DateTimeOffset? PeriodEnd { get; set; }

    [Column("reminder_date")]
    public DateTimeOffset? ReminderDate { get; set; }

    public decimal? GetUnusedPeriodAmount() => GetUnusedPeriodAmount(DateTimeOffset.UtcNow);

    public decimal? GetUnusedPeriodAmount(DateTimeOffset now)
    {
        if (PeriodEnd is { } pe &&
            pe - now > TimeSpan.Zero &&
            PaidAmount is { } pa)
        {
            var total = pe - PlanStarted;
            var remaining = pe - now;
            var unused = (decimal)(remaining.TotalMilliseconds / total.TotalMilliseconds);
            return pa * unused;
        }

        return null;
    }

    [Column("optimistic_activation")]
    public bool OptimisticActivation { get; set; }

    [Column("trial_end")]
    public DateTimeOffset? TrialEnd { get; set; }

    [NotMapped]
    public DateTimeOffset? NextPaymentDue => PeriodEnd ?? TrialEnd;

    [Column("grace_period_end")]
    public DateTimeOffset? GracePeriodEnd { get; set; }

    [Column("auto_renew")]
    public bool AutoRenew { get; set; } = true;

    [Required]
    [Column("active")]
    public bool IsActive { get; set; }

    [Column("payment_reminder_days")]
    public int? PaymentReminderDays { get; set; }

    [Required]
    [Column("payment_reminded")]
    public bool PaymentReminded { get; set; }

    [Required]
    [Column("suspended")]
    public bool IsSuspended { get; set; }

    public List<SubscriberCredit> Credits { get; set; } = null!;

    [Column("test_account")]
    public bool TestAccount { get; set; }

    [Column("suspension_reason")]
    public string? SuspensionReason { get; set; }

    [NotMapped]
    public bool CanStartNextPlan => CanStartNextPlanEx(false);

    public bool CanStartNextPlanEx(bool newSubscriber) => this is
    {
        Phase: not PhaseTypes.Normal,
        NextPlan:
        {
            Status: Data.Subscriptions.PlanData.PlanStatus.Active
        },
        IsSuspended: false
    }
    // If we stay on the same plan, check that the next plan is renwable
    && (newSubscriber || this.PlanId != this.NextPlan.Id || this.IsNextPlanRenewable);

    [NotMapped]
    public bool IsNextPlanRenewable => this.NextPlan is { Renewable: true, Status: Data.Subscriptions.PlanData.PlanStatus.Active };

    public PhaseTypes GetExpectedPhase(DateTimeOffset time)
        => this switch
        {
            { TrialEnd: { } te } when time < te => PhaseTypes.Trial,
            { PeriodEnd: { } pe } when time < pe => PhaseTypes.Normal,
            { GracePeriodEnd: { } gpe } when time < gpe => PhaseTypes.Grace,
            { Plan: { RecurringType: PlanData.RecurringInterval.Lifetime }, PaidAmount: not null } => PhaseTypes.Normal,
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
        var b = builder.Entity<SubscriberData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.Property(x => x.PaymentReminderDays);
        b.Property(x => x.Phase)
            .HasSentinel(PhaseTypes.Expired)
            .HasDefaultValueSql("'Expired'::TEXT").HasConversion<string>();
        b.Property(x => x.PlanStarted).HasDefaultValueSql("now()");
        b.Property(x => x.IsActive).HasDefaultValue(false);
        b.Property(x => x.IsSuspended).HasDefaultValue(false);
        b.Property(x => x.AutoRenew).HasDefaultValue(true);
        b.Property(x => x.PaymentReminded).HasDefaultValue(false);
        b.HasIndex(c => new { c.OfferingId, c.CustomerId })
            .IsUnique();
        b.Property(x => x.TestAccount).HasDefaultValue(false);
        b.HasOne(x => x.NewPlan).WithMany().HasForeignKey(x => x.NewPlanId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Plan).WithMany(x => x.Subscriptions).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Offering).WithMany(x => x.Subscribers).HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
    }

    public void StartNextPlan(DateTimeOffset now, bool trial = false)
    {
        var plan = NextPlan;
        Plan = plan;
        PlanId = plan.Id;

        NewPlan = null;
        NewPlanId = null;
        PaymentReminded = false;

        if (trial)
        {
            PlanStarted = now;
            PeriodEnd = null;
            TrialEnd = now.AddDays(plan.TrialDays);
            ReminderDate = TrialEnd - TimeSpan.FromDays(PaymentReminderDaysOrDefault);
            GracePeriodEnd = null;
            PaidAmount = null;
        }
        else
        {
            var currentPhase = this.GetExpectedPhase(now);
            var startDate = (currentPhase, this) switch
            {
                (PhaseTypes.Grace, { PeriodEnd: { } pe }) => pe,
                // If the user was on trial, give him for free until the end of the trial period.
                (PhaseTypes.Trial, { TrialEnd: { } te }) => te,
                _ => now
            };

            (PeriodEnd, GracePeriodEnd) = plan.GetPeriodEnd(startDate);
            ReminderDate = PeriodEnd - TimeSpan.FromDays(PaymentReminderDaysOrDefault);
            PlanStarted = now;
            TrialEnd = null;
            PaidAmount = plan.Price;
        }
    }

    [NotMapped]
    public int PaymentReminderDaysOrDefault => PaymentReminderDays ?? Plan.Offering.DefaultPaymentRemindersDays;

    public string ToNiceString()
        => $"{this.Customer?.GetPrimaryIdentity()} ({CustomerId})";

    public NewPlanScopeDisposable NewPlanScope(PlanData checkoutPlan)
    {
        var original = NewPlan;
        NewPlan = checkoutPlan;
        return new(this, original);
    }

    public class NewPlanScopeDisposable(SubscriberData subscriber, PlanData? originalPlan) : IDisposable
    {
        public bool IsCommitted { get; private set; }
        public void Commit() => IsCommitted = true;

        public void Dispose()
        {
            if (IsCommitted)
                return;
            subscriber.NewPlan = originalPlan;
            subscriber.NewPlanId = originalPlan?.Id;
        }
    }
    [NotMapped]
    public CustomerSelector CustomerSelector => CustomerSelector.ById(CustomerId);
}
