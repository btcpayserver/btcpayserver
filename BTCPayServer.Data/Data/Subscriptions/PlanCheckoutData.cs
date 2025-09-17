#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subscriptions_plan_checkouts")]
public class PlanCheckoutData : BaseEntityData
{
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

    [Column("subscriber_id")]
    public long? SubscriberId { get; set; }

    [ForeignKey(nameof(SubscriberId))]
    public SubscriberData? Subscriber { get; set; }

    [Column("invoice_metadata", TypeName = "jsonb")]
    public string InvoiceMetadata { get; set; } = "{}";

    [Column("new_subscriber_metadata", TypeName = "jsonb")]
    public string NewSubscriberMetadata { get; set; } = "{}";

    public string? GetRedirectUrl()
    {
        if (SuccessRedirectUrl is null)
            return null;
        // Add ?checkoutId=...&planId=... to the redirect URL
        try
        {
            var uriBuilder = new UriBuilder(SuccessRedirectUrl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["checkoutId"] = Id;
            query["planId"] = PlanId;
            uriBuilder.Query = query.ToString();
            return uriBuilder.ToString();
        }
        catch (UriFormatException) { return null; }
    }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<PlanCheckoutData>();
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("plancheckout"));

        b.Property(x => x.InvoiceMetadata).HasColumnName("invoice_metadata").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.NewSubscriberMetadata).HasColumnName("new_subscriber_metadata").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        b.Property(x => x.IsTrial).HasDefaultValue(false);
        b.HasOne(x => x.Plan).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subscriber).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Invoice).WithMany().OnDelete(DeleteBehavior.SetNull);
    }
}
