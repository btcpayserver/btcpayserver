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

    public static async Task<SubscriberData?> GetOrCreateByCustomerId(this DbSet<SubscriberData> subs, string custId, string planId)
    {
        var member = await subs.GetByCustomerId(custId);
        if (member != null)
            return member.PlanId == planId ? member : null;
        await subs.GetDbConnection().ExecuteScalarAsync<int>
        ("""
         INSERT INTO subscription_members (customer_id, plan_id) VALUES (@custId, @planId)
         ON CONFLICT DO NOTHING
         """, new { custId, planId });
        return await subs.GetByCustomerId(custId, planId);
    }

    private static Task<SubscriberData?> GetByCustomerId(this DbSet<SubscriberData> dbSet, string custId, string? planId = null)
        => planId switch
        {
            {} => dbSet.Include(p => p.Plan)
                        .Where(c => c.CustomerId == custId && c.PlanId == planId)
                        .FirstOrDefaultAsync(),
            _ => dbSet.Include(p => p.Plan).Where(c => c.CustomerId == custId).FirstOrDefaultAsync()
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
