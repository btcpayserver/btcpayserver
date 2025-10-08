using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250501000000_storetemplate")]
    public partial class storetemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migrates the old `Default Currency` settings from Server Settings to a Default Store Template.
            migrationBuilder.Sql(
                """
                UPDATE "Settings"
                SET "Value" =
                    jsonb_set(
                        -- remove "DefaultCurrency", create a template
                        ("Value" - 'DefaultCurrency') || '{"DefaultStoreTemplate":{"blob":{}}}'::JSONB,
                        -- path to insert the new nested value
                        '{DefaultStoreTemplate,blob,defaultCurrency}',
                        -- extract the old value of "DefaultCurrency"
                        to_jsonb("Value"->'DefaultCurrency'),
                        true
                    )
                WHERE "Id" = 'BTCPayServer.Services.PoliciesSettings'
                  AND "Value" ? 'DefaultCurrency' AND "Value"->>'DefaultCurrency' != 'USD';

                UPDATE "Settings"
                SET "Value" = "Value" - 'DefaultCurrency'
                WHERE "Id" = 'BTCPayServer.Services.PoliciesSettings'
                  AND "Value" ? 'DefaultCurrency';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
