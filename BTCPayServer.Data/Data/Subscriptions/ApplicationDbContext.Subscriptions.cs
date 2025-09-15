using BTCPayServer.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public partial class ApplicationDbContext
{
    public DbSet<EntitlementData> Entitlements { get; set; }
    public DbSet<PlanEntitlementData> PlanEntitlements { get; set; }
    public DbSet<OfferingData> Offerings { get; set; }
    public DbSet<SubscriberData> Subscribers { get; set; }
    public DbSet<PlanData> Plans { get; set; }
    public DbSet<PlanCheckoutData> PlanCheckouts { get; set; }

    void OnSubscriptionsModelCreating(ModelBuilder builder)
    {
        PlanCheckoutData.OnModelCreating(builder, Database);
        EntitlementData.OnModelCreating(builder, Database);
        PlanEntitlementData.OnModelCreating(builder, Database);
        OfferingData.OnModelCreating(builder, Database);
        SubscriberData.OnModelCreating(builder, Database);
        PlanData.OnModelCreating(builder, Database);
    }
}
