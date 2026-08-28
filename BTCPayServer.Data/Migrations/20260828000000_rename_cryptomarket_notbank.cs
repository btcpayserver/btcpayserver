using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828000000_rename_cryptomarket_notbank")]
public class rename_cryptomarket_notbank : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Stores"
            SET "StoreBlob" = replace("StoreBlob"::text, 'cryptomarket', 'notbank')::jsonb
            WHERE "StoreBlob"::text LIKE '%cryptomarket%';
            """);

        migrationBuilder.Sql("""
            UPDATE "Settings"
            SET "Value" = replace("Value"::text, 'cryptomarket', 'notbank')::jsonb
            WHERE "Id" = 'BTCPayServer.Services.PoliciesSettings'
              AND "Value"::text LIKE '%cryptomarket%';
            """);
    }
}
