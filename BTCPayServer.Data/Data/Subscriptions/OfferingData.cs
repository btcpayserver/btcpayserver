#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subscriptions_offerings")]
public class OfferingData : BaseEntityData
{
    [Key]
    [Required]
    public string Id { get; set; } = null!;

    [Required]
    [Column("app_id")]
    public string AppId { get; set; } = null!;

    [ForeignKey(nameof(AppId))]
    public AppData App { get; set; } = null!;

    public List<EntitlementData> Entitlements { get; set; } = null!;
    public List<PlanData> Plans { get; set; } = null!;
    public List<SubscriberData> Subscribers { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<OfferingData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix("offering"));
        b.HasOne(o => o.App).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
