#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public static partial class ApplicationDbContextExtensions
{
    public static async Task<PlanData?> GetPlanFromId(this DbSet<PlanData> plans, string planId, string? offeringId = null, string? storeId = null)
    {
        var plan = await plans
            .Include(o => o.Offering).ThenInclude(o => o.App).ThenInclude(o => o.StoreData)
            .Include(o => o.PlanChanges).ThenInclude(o => o.PlanChange)
            .Where(p => p.Id == planId)
            .FirstOrDefaultAsync();
        if (offeringId is not null && plan?.OfferingId != offeringId)
            return null;
        if (storeId is not null && plan?.Offering.App.StoreDataId != storeId)
            return null;
        if (plan is not null)
            await plan.EnsureFeatureLoaded(plans);
        return plan;
    }

    public static async Task<bool> HasFeature(this DbSet<PlanData> plans, string planId, string featureCustomId)
    {
        var connection = plans.GetDbConnection();
        return await connection.ExecuteScalarAsync<bool>("""
                                              SELECT true FROM subs_plans_features pe
                                              JOIN subs_features e ON e.id = pe.feature_id
                                              WHERE pe.plan_id = @planId AND e.custom_id = @featureCustomId
                                              """, new{ planId, featureCustomId });
    }

    public static async Task FetchPlanFeaturesAsync<T>(this DbSet<T> ctx, IEnumerable<PlanData> plans) where T : class
    {
        var planIds = plans.Select(p => p.Id).Distinct().ToArray();
        var result = await ctx.GetDbConnection()
            .QueryAsync<(
                string Id,
                long[] EIds,
                string[] ECIds,
                string[] EDesc,
                string[] EName)>
                (
                """
                    SELECT pId,
                           array_agg(spe.feature_id),
                           array_agg(se.custom_id),
                           array_agg(se.description)
                    FROM unnest(@planIds) pId
                    JOIN subs_plans_features spe ON spe.plan_id = pId
                    JOIN subs_features se ON se.id = spe.feature_id
                    GROUP BY 1
                """, new { planIds }
                );
        var res = result.ToDictionary(x => x.Id, x => x);
        foreach (var plan in plans)
        {
            plan.PlanFeatures = new();
            if (res.TryGetValue(plan.Id, out var r))
            {
                for (int i = 0; i < r.ECIds.Length; i++)
                {
                    var pe = new PlanFeatureData();
                    pe.Plan = plan;
                    pe.PlanId = plan.Id;
                    pe.FeatureId = r.EIds[i];
                    pe.Feature = new()
                    {
                        Id = r.EIds[i],
                        CustomId = r.ECIds[i],
                        Description = r.EDesc[i],
                    };
                    plan.PlanFeatures.Add(pe);
                }
            }
        }
    }


    public static Task FetchPlanFeaturesAsync<T>(this DbSet<T> ctx, PlanData plan) where T : class
        => FetchPlanFeaturesAsync(ctx, new[] { plan });


    public static async Task<OfferingData?> GetOfferingData(this DbSet<OfferingData> offerings, string offeringId, string? storeId = null)
    {
        var offering = offerings.IncludeAll();

        var o = await offering
                .Where(o => o.Id == offeringId)
                .FirstOrDefaultAsync();
        if (storeId != null &&  o?.App.StoreDataId != storeId)
            return null;
        return o;
    }

    public static IQueryable<OfferingData> IncludeAll(this DbSet<OfferingData> offerings)
    => offerings
        .Include(o => o.Features)
        .Include(o => o.Plans)
        .Include(o => o.App)
        .ThenInclude(o => o.StoreData)
        .AsSplitQuery();

    public static async Task<PlanCheckoutData?> GetCheckout(this DbSet<PlanCheckoutData> checkouts, string checkoutId)
    {
        var checkout = await checkouts
            .Include(x => x.Plan).ThenInclude(x => x.Offering).ThenInclude(x => x.App).ThenInclude(x => x.StoreData)
            .Include(x => x.Invoice)
            .Include(x => x.Subscriber).ThenInclude(x => x!.Customer).ThenInclude(x => x.CustomerIdentities)
            .Include(x => x.Subscriber).ThenInclude(x => x!.Credits)
            .Include(x => x.Subscriber).ThenInclude(x => x!.Plan)
            .Where(c => c.Id == checkoutId)
            .FirstOrDefaultAsync();
        if (checkout is not null)
            await FetchPlanFeaturesAsync(checkouts, checkout.Plan);
        return checkout;
    }

    public static async Task<(SubscriberData?, bool Created)> GetOrCreateByCustomerId(this DbSet<SubscriberData> subs, string custId, string offeringId, string planId, bool? optimisticActivation, bool testAccount, JObject? newMemberMetadata = null)
    {
        var member = await subs.GetByCustomerId(custId, offeringId);
        if (member != null)
            return member.PlanId == planId ? (member, false) : (null, false);
        var membId = await subs.GetDbConnection().ExecuteScalarAsync<long?>
        ("""
         INSERT INTO subs_subscribers (customer_id, offering_id, plan_id, optimistic_activation, plan_started, test_account, metadata) VALUES (@custId, @offeringId, @planId, @optimisticActivation, @now, @testAccount, @metadata::JSONB)
         ON CONFLICT DO NOTHING
         RETURNING id
         """, new { custId, planId, offeringId, now = DateTimeOffset.UtcNow, optimisticActivation = optimisticActivation ?? false, testAccount, metadata = newMemberMetadata?.ToString() ?? "{}" });

        member = membId is null ? null : await subs.GetById(membId.Value);
        return member?.PlanId == planId ? (member, true) : (null, false);
    }

    public static Task<PortalSessionData?> GetActiveById(this IQueryable<PortalSessionData> sessions, string sessionId)
        => sessions.IncludeAll()
            .Where(s => s.Id == sessionId && DateTimeOffset.UtcNow < s.Expiration).FirstOrDefaultAsync();
    public static Task<PortalSessionData?> GetById(this IQueryable<PortalSessionData> sessions, string sessionId)
        => sessions.IncludeAll()
            .Where(s => s.Id == sessionId).FirstOrDefaultAsync();

    public static IIncludableQueryable<PortalSessionData, StoreData> IncludeAll(this IQueryable<PortalSessionData> sessions)
    => sessions
        .Include(s => s.Subscriber).ThenInclude(s => s.Customer).ThenInclude(s => s.CustomerIdentities)
        .Include(s => s.Subscriber).ThenInclude(s => s.Credits)
        .Include(s => s.Subscriber).ThenInclude(s => s.Plan).ThenInclude(s => s.PlanChanges).ThenInclude(s => s.PlanChange)
        .Include(s => s.Subscriber).ThenInclude(s => s.Plan).ThenInclude(s => s.Offering).ThenInclude(s => s.App).ThenInclude(s => s.StoreData);

    public static async Task<SubscriberData?> GetByCustomerId(this DbSet<SubscriberData> dbSet, string custId, string offeringId, string? planId = null,
        string? storeId = null)
    {
        var result = await dbSet.IncludeAll()
                        .Where(c => c.CustomerId == custId && c.OfferingId == offeringId).FirstOrDefaultAsync();

        if ((result is null) ||
            (storeId != null && result.Plan?.Offering?.App?.StoreDataId != storeId) ||
            (planId != null && result.PlanId != planId))
            return null;
        await FetchPlanFeaturesAsync(dbSet, result.Plan);
        return result;
    }

    public static IIncludableQueryable<SubscriberData, List<SubscriberCredit>> IncludeAll(this IQueryable<SubscriberData> subscribers)
    => subscribers
            .Include(p => p.NewPlan)
            .Include(p => p.Plan).ThenInclude(p => p.Offering).ThenInclude(p => p.App)
            .Include(m => m.Customer).ThenInclude(c => c.CustomerIdentities)
            .Include(s => s.Credits);

    public static async Task<SubscriberData?> GetById(this DbSet<SubscriberData> subscribers, long id)
    {
        var sub = await subscribers.IncludeAll().Where(s => s.Id == id).FirstOrDefaultAsync();
        if (sub != null)
            await FetchPlanFeaturesAsync(subscribers, sub.Plan);
        return sub;
    }

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
        else if (selector is CustomerSelector.ExternalRef { Ref: {} externalRef })
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

    public static async Task<SubscriberData?> GetBySelector(this DbSet<SubscriberData> subscribers, string offeringId, CustomerSelector selector, string? storeId = null)
    {
        var ctx = (ApplicationDbContext)subscribers.GetDbContext();
        storeId ??= await ctx.Offerings
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
        return await subscribers.IncludeAll().Where(s => s.OfferingId == offeringId && s.CustomerId == customerId).FirstOrDefaultAsync();
    }

    public static Task<CustomerData?> GetBySelector(this IQueryable<CustomerData> customers, string storeId, CustomerSelector selector)
    {
        customers = customers.Where(c => c.StoreId == storeId);
        return (selector switch
        {
            CustomerSelector.Id { CustomerId: {} id } => customers.Where(c => c.Id == id),
            CustomerSelector.ExternalRef { Ref: {} externalRef } => customers.Where(c => c.ExternalRef == externalRef),
            CustomerSelector.Identity { Type: { } type, Value: { } value } => customers.Where(c => c.CustomerIdentities.Any(cust => cust.Type == type && cust.Value == value)),
            _ => throw new NotSupportedException()
        }).FirstOrDefaultAsync();
    }
}
