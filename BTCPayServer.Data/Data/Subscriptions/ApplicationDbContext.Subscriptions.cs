using BTCPayServer.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public partial class ApplicationDbContext
{
    public DbSet<FeatureData> Features { get; set; }
    public DbSet<PlanFeatureData> PlanFeatures { get; set; }
    public DbSet<OfferingData> Offerings { get; set; }
    public DbSet<SubscriberData> Subscribers { get; set; }
    public DbSet<SubscriberCredit> Credits { get; set; }
    public DbSet<PlanData> Plans { get; set; }
    public DbSet<PlanChangeData> PlanChanges { get; set; }
    public DbSet<PlanCheckoutData> PlanCheckouts { get; set; }
    public DbSet<SubscriberInvoiceData> SubscribersInvoices { get; set; }

    public DbSet<PortalSessionData> PortalSessions { get; set; }

    public DbSet<SubscriberCreditHistoryData> SubscriberCreditHistory { get; set; }

    void OnSubscriptionsModelCreating(ModelBuilder builder)
    {
        SubscriberCreditHistoryData.OnModelCreating(builder, Database);
        PlanChangeData.OnModelCreating(builder, Database);
        PortalSessionData.OnModelCreating(builder, Database);
        PlanCheckoutData.OnModelCreating(builder, Database);
        FeatureData.OnModelCreating(builder, Database);
        PlanFeatureData.OnModelCreating(builder, Database);
        OfferingData.OnModelCreating(builder, Database);
        SubscriberData.OnModelCreating(builder, Database);
        SubscriberInvoiceData.OnModelCreating(builder, Database);
        SubscriberCredit.OnModelCreating(builder, Database);
        PlanData.OnModelCreating(builder, Database);
    }
}
