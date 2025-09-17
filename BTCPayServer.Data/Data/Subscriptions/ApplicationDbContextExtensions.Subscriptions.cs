#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public static partial class ApplicationDbContextExtensions
{
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
            .Include(x => x.Plan).ThenInclude(x => x.PlanEntitlements).ThenInclude(x => x.Entitlement)
            .Include(x => x.Subscriber).ThenInclude(x => x!.Customer)
            .Where(c => c.Id == checkoutId)
            .FirstOrDefaultAsync();
    }

    public static async Task<(SubscriberData?, bool Created)> GetOrCreateByCustomerId(this DbSet<SubscriberData> subs, string custId, string offeringId, string planId, JObject? newMemberMetadata = null)
    {
        var member = await subs.GetByCustomerId(custId, offeringId);
        if (member != null)
            return member.PlanId == planId ? (member, false) : (null, false);
        var membId = await subs.GetDbConnection().ExecuteScalarAsync<long?>
        ("""
         INSERT INTO subscriptions_subscribers (customer_id, offering_id, plan_id, metadata) VALUES (@custId, @offeringId, @planId, @metadata::JSONB)
         ON CONFLICT DO NOTHING
         RETURNING id
         """, new { custId, planId, offeringId, metadata = newMemberMetadata?.ToString() ?? "{}" });

        member = membId is null ? null : await subs.FindAsync([membId.Value]);
        return member?.PlanId == planId ? (member, true) : (null, false);
    }

    public static Task<SubscriberData?> GetByCustomerId(this DbSet<SubscriberData> dbSet, string custId, string offeringId, string? planId = null)
        => planId switch
        {
            not null => dbSet.Include(p => p.Plan)
                .Where(c => c.CustomerId == custId && c.OfferingId == offeringId && c.PlanId == planId)
                .FirstOrDefaultAsync(),
            _ => dbSet.Include(p => p.Plan).Where(c => c.CustomerId == custId && c.OfferingId == offeringId).FirstOrDefaultAsync()
        };

    public static async Task<CustomerData> GetOrUpdate(this DbSet<CustomerData> dbSet, string storeId, CustomerSelector selector)
    {
        var cust = await GetBySelector(dbSet, storeId, selector);
        if (cust != null)
            return cust;
        string? custId;
        if (selector is CustomerSelector.Id { CustomerId: {} id })
        {
            custId = await dbSet.GetDbConnection()
                .ExecuteScalarAsync<string>
                ("""
                 INSERT INTO customers (id, store_id) VALUES (@id, @storeId)
                 ON CONFLICT DO NOTHING
                 RETURNING id
                 """, new { id, storeId });
        }
        else if (selector is CustomerSelector.ExternalRef { ExtenalRef: {} externalRef })
        {
            custId = await dbSet.GetDbConnection()
                .ExecuteScalarAsync<string>
                ("""
                 INSERT INTO customers (id, external_ref, store_id) VALUES (@id, @externalRef, @storeId)
                 ON CONFLICT (store_id, external_ref) DO NOTHING
                 RETURNING id
                 """, new { id = CustomerData.GenerateId(), externalRef, storeId });
        }
        else if (selector is CustomerSelector.Identity { Type: { } type, Value: { } value })
        {
            custId = await dbSet.GetDbConnection()
                .ExecuteScalarAsync<string>
                ("""
                 WITH ins_cust AS (
                 INSERT INTO customers (id, store_id) VALUES (@id, @storeId)
                 RETURNING id),
                 ins_identity AS (
                 INSERT INTO customers_identities (customer_id, type, value)
                 SELECT id, @type, @value
                 FROM ins_cust
                 RETURNING customer_id
                 )
                 SELECT customer_id FROM ins_identity;
                 """, new { id = CustomerData.GenerateId(), storeId, type, value });
        }
        else
        {
            throw new NotSupportedException(selector.ToString());
        }

        return
            (custId is null ?
                await GetBySelector(dbSet, storeId, selector) :
                await GetById(dbSet, storeId, custId)) ?? throw new InvalidOperationException("Customer not found");
    }

    private static Task<CustomerData?> GetById(this IQueryable<CustomerData> customers, string storeId, string custId)
        => GetBySelector(customers, storeId, CustomerSelector.ById(custId));

    public static async Task<SubscriberData?> GetBySelector(this DbSet<SubscriberData> subscribers, string offeringId, CustomerSelector selector)
    {
        var ctx = (ApplicationDbContext)subscribers.GetDbContext();
        var storeId = await ctx.Offerings
            .Where(o => o.Id == offeringId)
            .Select(o => o.App.StoreDataId)
            .FirstOrDefaultAsync();
        if (storeId is null)
            return null;

        string? customerId = null;
        customerId = selector is CustomerSelector.Id { CustomerId: {} id } ? id
                        : (await ctx.Customers.GetBySelector(storeId, selector))?.Id;
        if (customerId is null)
            return null;
        return await subscribers.Where(s => s.OfferingId == offeringId && s.CustomerId == customerId).FirstOrDefaultAsync();
    }

    public static Task<CustomerData?> GetBySelector(this IQueryable<CustomerData> customers, string storeId, CustomerSelector selector)
    {
        customers = customers.Where(c => c.StoreId == storeId);
        return (selector switch
        {
            CustomerSelector.Id { CustomerId: {} id } => customers.Where(c => c.Id == id),
            CustomerSelector.ExternalRef { ExtenalRef: {} externalRef } => customers.Where(c => c.ExternalRef == externalRef),
            CustomerSelector.Identity { Type: { } type, Value: { } value } => customers.Where(c => c.CustomerIdentities.Any(cust => cust.Type == type && cust.Value == value)),
            _ => throw new NotSupportedException()
        }).FirstOrDefaultAsync();
    }
}
