#nullable enable
using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public static partial class ApplicationDbContextExtensions
{
    public static DbContext GetDbContext<T>(this DbSet<T> dbSet) where T : class
    {
        var infrastructure = dbSet as IInfrastructure<IServiceProvider>;
        var serviceProvider = infrastructure.Instance;
        var currentDbContext = (ICurrentDbContext)serviceProvider.GetService(typeof(ICurrentDbContext))!;
        return currentDbContext.Context;
    }

    public static DbConnection GetDbConnection<T>(this DbSet<T> dbSet) where T : class
        => dbSet.GetDbContext().Database.GetDbConnection();

    public static async Task<PlanData?> GetPlanFromId(this DbSet<PlanData> plans, string planId)
    {
        var plan = await plans
            .Include(o => o.Offering).ThenInclude(o => o.App).ThenInclude(o => o.StoreData)
            .Include(o => o.PlanEntitlements).ThenInclude(o => o.Entitlement)
            .Where(p => p.Id == planId)
            .FirstOrDefaultAsync();
        return plan;
    }

    public static async Task<OfferingData?> GetOfferingData(this DbSet<OfferingData> offerings, string offeringId, string? storeId = null)
    {
        var offering = offerings
            .Include(o => o.Entitlements)
            .Include(o => o.App)
            .ThenInclude(o => o.StoreData);

        if (storeId != null)
            return await offering
                .Where(o => o.Id == offeringId && o.App.StoreDataId == storeId)
                .FirstOrDefaultAsync();
        else
            return await offering
                .Where(o => o.Id == offeringId)
                .FirstOrDefaultAsync();
    }

    public static async Task<PlanCheckoutData?> GetCheckout(this DbSet<PlanCheckoutData> checkouts, string checkoutId)
    {
        return await checkouts
            .Include(x => x.Plan).ThenInclude(x => x.Offering).ThenInclude(x => x.App).ThenInclude(x => x.StoreData)
            .Include(x => x.Subscriber).ThenInclude(x => x!.Customer)
            .Where(c => c.Id == checkoutId)
            .FirstOrDefaultAsync();
    }

    public static async Task<SubscriberData?> GetOrCreateByCustomerId(this DbSet<SubscriberData> subs, string custId, string offeringId, string planId)
    {
        var member = await subs.GetByCustomerId(custId, offeringId);
        if (member != null)
            return member.PlanId == planId ? member : null;
        await subs.GetDbConnection().ExecuteScalarAsync<int>
        ("""
         INSERT INTO subscriptions_subscribers (customer_id, offering_id, plan_id) VALUES (@custId, @offeringId, @planId)
         ON CONFLICT DO NOTHING
         """, new { custId, planId, offeringId });
        return await subs.GetByCustomerId(custId, offeringId, planId);
    }

    public static Task<SubscriberData?> GetByCustomerId(this DbSet<SubscriberData> dbSet, string custId, string offeringId, string? planId = null)
        => planId switch
        {
            not null => dbSet.Include(p => p.Plan)
                .Where(c => c.CustomerId == custId && c.OfferingId == offeringId && c.PlanId == planId)
                .FirstOrDefaultAsync(),
            _ => dbSet.Include(p => p.Plan).Where(c => c.CustomerId == custId && c.OfferingId == offeringId).FirstOrDefaultAsync()
        };

    public static async Task<CustomerData> GetOrUpdate(this DbSet<CustomerData> dbSet, string storeId, string email)
    {
        var cust = await GetByEmail(dbSet, storeId, email);
        if (cust != null)
            return cust;
        var custId = await dbSet.GetDbConnection()
            .ExecuteScalarAsync<string>
            ("""
             INSERT INTO customers (id, store_id, email) VALUES (@id, @storeId, @email)
             ON CONFLICT (store_id, email) DO NOTHING
             RETURNING id
             """, new { id = CustomerData.GenerateId(), storeId, email }) ?? string.Empty;
        return await GetById(dbSet, storeId, custId) ?? throw new InvalidOperationException("Customer not found");
    }

    private static Task<CustomerData?> GetById(DbSet<CustomerData> dbSet, string storeId, string custId)
        => dbSet.Where(c => c.StoreId == storeId && c.Id == custId).FirstOrDefaultAsync();

    private static Task<CustomerData?> GetByEmail(DbSet<CustomerData> dbSet, string storeId, string email)
        => dbSet.Where(c => c.StoreId == storeId && c.Email == email).FirstOrDefaultAsync();
}
