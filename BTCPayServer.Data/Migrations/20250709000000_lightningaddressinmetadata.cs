using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250709000000_lightningaddressinmetadata")]
    public partial class lightningaddressinmetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Invoices"
                SET "Blob2" = jsonb_set(
                  "Blob2",
                  '{metadata}',
                  "Blob2"->'metadata' || jsonb_build_object(
                    'lightningAddress',
                    "Blob2"->'prompts'->'BTC-LNURL'->'details'->>'consumedLightningAddress'
                  )
                )
                WHERE
                  "Status" != 'Expired'
                  AND "Blob2"->'prompts'->'BTC-LNURL'->'details'->'consumedLightningAddress' IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
