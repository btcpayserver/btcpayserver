#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

[Table("subscription_stats")]
public class SubscriptionStatsData
{
    [Key]
    [Column("store_id")]
    public string StoreId { get; set; } = null!;

    [ForeignKey("StoreId")]
    public StoreData Store { get; set; } = null!;

    [Column("members_count")]
    public long ActiveMembersCount { get; set; }

    [Column("subs_count")]
    public long ActiveSubscriptionsCount { get; set; }

    [Column("total_revenue")]
    public decimal TotalRevenue { get; set; }
}
