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

    public List<EntitlementData> Entitlements { get; set; } = null!;
    public List<PlanData> Plans { get; set; } = null!;
    public List<SubscriptionData> Subscriptions { get; set; } = null!;

    [Required]
    [Column("store_id")]
    public string StoreId { get; set; } = null!;

    [ForeignKey(nameof(StoreId))]
    public StoreData Store { get; set; } = null!;

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<OfferingData>();
        OnModelCreateBase(b, builder, databaseFacade);
    }
}
