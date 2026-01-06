using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Monetization.Migrations;

public class RemoveDeletedApplicationIdIdentities() : MigrationBase<ApplicationDbContext>("20260106_cleanupappidentities")
{
    public override async Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.GetDbConnection()
            .ExecuteAsync("""
                          WITH orphan AS (
                              SELECT value FROM customers_identities ci
                              LEFT JOIN "AspNetUsers" u ON u."Id" = value
                              WHERE ci.type = @type AND u."Id" IS NULL
                          )
                          DELETE FROM customers_identities ci
                              USING orphan u
                                  WHERE ci.type = @type AND u.value = ci.value;
                          """, new { type = Monetization.SubscriberDataExtensions.IdentityType });
    }
}
