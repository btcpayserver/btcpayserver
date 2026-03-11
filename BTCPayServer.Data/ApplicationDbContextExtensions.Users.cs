using System.Threading.Tasks;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public static partial class ApplicationDbContextExtensions
{
    /// <summary>
    /// StoreBlob.NoActiveUser is set up to `true` if users have a store where there are
    /// no active users
    /// </summary>
    /// <param name="dbSet"></param>
    /// <param name="userIds"></param>
    public static Task UpdateStoreNoActiveUserForUsers(this DbSet<ApplicationUser> dbSet, string[] userIds)
    {
        DynamicParameters parameters = new();
        parameters.Add("userIds", userIds);
        return dbSet.UpdateStoreNoActiveUserCore("""
                                                       SELECT DISTINCT us."StoreDataId" AS store_id
                                                       FROM unnest(@userIds::text[]) AS u("UserId")
                                                       JOIN "UserStore" us ON us."ApplicationUserId" = u."UserId"
                                                       """, parameters);
    }

    public static Task UpdateStoreNoActiveUserForStores(this DbSet<ApplicationUser> dbSet, string[] storeIds)
    {
        DynamicParameters parameters = new();
        parameters.Add("stores", storeIds);
        return dbSet.UpdateStoreNoActiveUserCore("""
                                                       SELECT DISTINCT store_id
                                                       FROM unnest(@stores::text[]) AS u(store_id)
                                                       """, parameters);
    }

    public static Task UpdateStoreNoActiveUserCore(this DbSet<ApplicationUser> dbSet, string selectStoresSql, DynamicParameters parameters)
    => dbSet.GetDbConnection()
        .ExecuteAsync(GetUpdateStoreNoActiveUserQuery(selectStoresSql), parameters);

    internal static string GetUpdateStoreNoActiveUserQuery(string selectStoresSql)
        => $$"""
             WITH
             -- Select all the stores belonging to the userIds
             stores AS (
                 {{selectStoresSql}}
             ),
             -- Count all active users of the store
             active_users AS (
                 SELECT s."Id",
                        COUNT(u."Id") FILTER (WHERE u."LockoutEnd" IS NULL OR u."LockoutEnd" <= NOW()) AS active_users
                 FROM stores st
                 JOIN "Stores" s ON s."Id" = st.store_id
                 LEFT JOIN "UserStore" us ON us."StoreDataId" = s."Id"
                 LEFT JOIN "AspNetUsers" u ON u."Id" = us."ApplicationUserId"
                 GROUP BY s."Id"
             ),
             -- If the total is 0, then the store should have noActiveUser set to true
             expected_disabled AS (
                 SELECT s."Id", COALESCE(s."StoreBlob"->'noActiveUser' = 'true'::JSONB, false) current_value, t.active_users = 0 expected_value FROM active_users t
                 JOIN "Stores" s ON s."Id" = t."Id"
             )
             -- Update only the stores not having expected values.
             UPDATE "Stores" s
             SET "StoreBlob" = jsonb_set("StoreBlob", '{noActiveUser}', to_jsonb(ed.expected_value))
             FROM expected_disabled ed
             WHERE s."Id" = ed."Id" AND ed.expected_value <> ed.current_value;
             """;
}
