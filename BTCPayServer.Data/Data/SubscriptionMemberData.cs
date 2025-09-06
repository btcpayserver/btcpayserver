#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

[Table("subscription_members")]
public class SubscriptionMemberData : BaseEntityData
{
    [Required]
    [Column("customer_id")]
    public string CustomerId { get; set; } = null!;

    [ForeignKey("id")]
    public CustomerData Customer { get; set; } = null!;

    [Required]
    [Column("plan_id")]
    public string PlanId { get; set; } = null!;

    [ForeignKey("id")]
    public SubscriptionPlanData Plan { get; set; } = null!;

    [Required]
    [Column("status")]
    public MemberStatus Status { get; set; } = MemberStatus.Inactive;


    public enum MemberStatus
    {
        Active,
        Inactive
    }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriptionMemberData>();
        b.Property(x => x.Status).HasConversion<string>();
    }
}
