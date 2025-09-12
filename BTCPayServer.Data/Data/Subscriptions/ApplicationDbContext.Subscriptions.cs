using BTCPayServer.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public partial class ApplicationDbContext
{
    public DbSet<EntitlementData> Entitlements { get; set; }
    public DbSet<SubscriptionEntitlementData> OfferingEntitlements { get; set; }
    public DbSet<OfferingData> Offerings { get; set; }
    public DbSet<SubscriptionData> Subscriptions { get; set; }
    public DbSet<PlanData> Plans { get; set; }

    void OnSubscriptionsModelCreating(ModelBuilder builder)
    {
        EntitlementData.OnModelCreating(builder, Database);
        SubscriptionEntitlementData.OnModelCreating(builder, Database);
        OfferingData.OnModelCreating(builder, Database);
        SubscriptionData.OnModelCreating(builder, Database);
        PlanData.OnModelCreating(builder, Database);
    }
}
