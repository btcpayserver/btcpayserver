using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260112034125_replace_coingecko")]
public class replace_coingecko  : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                             UPDATE "Stores"
                             SET "StoreBlob" = jsonb_set("StoreBlob", '{primaryRateSettings,preferredExchange}', 'null'::jsonb, true)
                             WHERE "StoreBlob"->'primaryRateSettings'->>'preferredExchange' = 'coingecko';
                             """);
        migrationBuilder.Sql("""
                             UPDATE "Stores"
                             SET "StoreBlob" = jsonb_set("StoreBlob", '{fallbackRateSettings,preferredExchange}', 'null'::jsonb, true)
                             WHERE "StoreBlob"->'fallbackRateSettings'->>'preferredExchange' = 'coingecko';
                             """);
    }
}
