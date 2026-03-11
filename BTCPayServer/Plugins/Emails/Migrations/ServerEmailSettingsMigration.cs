using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Emails.Migrations;

public class ServerEmailSettingsMigration() : MigrationBase<ApplicationDbContext>(("20251223_emailsettingsmigration"))
{
    public override async Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.GetDbConnection()
            .ExecuteAsync("""
                          INSERT INTO "Settings" ("Id", "Value")
                          SELECT
                            'BTCPayServer.Plugins.Emails.Services.EmailSettings',
                            "Value"
                          FROM "Settings"
                          WHERE "Id" = 'BTCPayServer.Services.Mails.EmailSettings'
                          ON CONFLICT ("Id") DO NOTHING;

                          DELETE FROM "Settings"
                          WHERE "Id" = 'BTCPayServer.Services.Mails.EmailSettings';
                          """);
    }
}
