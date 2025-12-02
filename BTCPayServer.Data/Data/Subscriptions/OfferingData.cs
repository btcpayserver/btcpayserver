#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AngleSharp.Html;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_offerings")]
public class OfferingData : BaseEntityData
{
    [Key]
    [Required]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Required]
    [Column("app_id")]
    public string AppId { get; set; } = null!;

    [ForeignKey(nameof(AppId))]
    public AppData App { get; set; } = null!;

    public List<FeatureData> Features { get; set; } = null!;
    public List<PlanData> Plans { get; set; } = null!;
    public List<SubscriberData> Subscribers { get; set; } = null!;

    [Column("success_redirect_url")]
    public string? SuccessRedirectUrl { get; set; }

    [Column("payment_reminder_days")]
    [Required]
    public int DefaultPaymentRemindersDays { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<OfferingData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.DefaultPaymentRemindersDays).HasDefaultValue(3);
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("offering"));
        b.HasOne(o => o.App).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
