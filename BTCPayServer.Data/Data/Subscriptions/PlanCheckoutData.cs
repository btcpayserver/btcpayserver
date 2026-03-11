#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Abstractions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_plan_checkouts")]
public class PlanCheckoutData : BaseEntityData
{
    public PlanCheckoutData()
    {

    }

    public PlanCheckoutData(SubscriberData subscriber, PlanData? plan = null)
    {
        plan ??= subscriber.Plan;
        NewSubscriber = false;
        Subscriber = subscriber;
        SubscriberId = subscriber.Id;
        Plan = plan;
        PlanId = plan.Id;
    }
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Column("invoice_id")]
    public string? InvoiceId { get; set; }

    [ForeignKey(nameof(InvoiceId))]
    public InvoiceData? Invoice { get; set; }

    [Column("success_redirect_url")]
    public string? SuccessRedirectUrl { get; set; }

    [Column("is_trial")]
    public bool IsTrial { get; set; }

    [Required]
    [Column("plan_id")]
    public string PlanId { get; set; } = null!;

    [ForeignKey(nameof(PlanId))]
    public PlanData Plan { get; set; } = null!;

    [Column("new_subscriber")]
    public bool NewSubscriber { get; set; }

    [Column("new_subscriber_email")]
    public string? NewSubscriberEmail { get; set; }

    /// <summary>
    /// Internal ID of the subscriber, do not expose outside, only use for querying.
    /// </summary>
    [Column("subscriber_id")]
    public long? SubscriberId { get; set; }

    [ForeignKey(nameof(SubscriberId))]
    public SubscriberData? Subscriber { get; set; }

    [Column("invoice_metadata", TypeName = "jsonb")]
    public string InvoiceMetadata { get; set; } = "{}";

    [Column("new_subscriber_metadata", TypeName = "jsonb")]
    public string NewSubscriberMetadata { get; set; } = "{}";

    [Column("test_account")]
    public bool TestAccount { get; set; }

    [Column("credited")]
    public decimal CreditedByInvoice { get; set; } = 0m;

    [Column("plan_started")]
    public bool PlanStarted { get; set; }

    [Column("refund_amount")]
    public decimal? RefundAmount { get; set; }

    [Column("on_pay")]
    public OnPayBehavior OnPay { get; set; }

    [Required]
    [Column("base_url", TypeName = "text")]
    public RequestBaseUrl BaseUrl { get; set; } = null!;

    [Required]
    [Column("expiration")]
    public DateTimeOffset Expiration { get; set; }

    [Column("credit_purchase")]
    public decimal? CreditPurchase { get; set; }

    public enum OnPayBehavior
    {
        /// <summary>
        /// Starts the plan if the phase is expired or grace, else, add to credit.
        /// </summary>
        SoftMigration,
        /// <summary>
        /// Starts the plan immediately. If a payment wasn't due yet, reimburse the unused part of the period,
        /// and start the plan.
        /// </summary>
        HardMigration
    }

    public string? GetRedirectUrl()
    {
        if (SuccessRedirectUrl is null)
            return null;
        // Add ?checkoutPlanId=... to the redirect URL
        try { return QueryHelpers.AddQueryString(SuccessRedirectUrl, "checkoutPlanId", Id); }
        catch (UriFormatException) { return null; }
    }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanCheckoutData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("plancheckout"));

        b.Property(x => x.InvoiceMetadata).HasColumnName("invoice_metadata").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.NewSubscriberMetadata).HasColumnName("new_subscriber_metadata").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        b.Property(x => x.BaseUrl)
            .HasConversion<string>(
                x => x.ToString(),
                x => RequestBaseUrl.FromUrl(x)
            );

        b.HasIndex(x => x.Expiration);
        b.Property(x => x.Expiration).HasDefaultValueSql("now() + interval '1 day'");
        b.Property(x => x.OnPay).HasDefaultValue(OnPayBehavior.SoftMigration).HasConversion<string>();
        b.Property(x => x.IsTrial).HasDefaultValue(false);
        b.HasOne(x => x.Plan).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subscriber).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Invoice).WithMany().OnDelete(DeleteBehavior.SetNull);
    }

    [NotMapped]
    public bool IsExpired => DateTimeOffset.UtcNow > Expiration;

    public string? GetEmail()
    {
        if (NewSubscriber)
            return NewSubscriberEmail;
        return Subscriber?.Customer.Email.Get();
    }
}
