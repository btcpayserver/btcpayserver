using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250508000000_fallbackrates")]
    public partial class fallbackrates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Stores"
                SET "StoreBlob" = "StoreBlob"
                    || jsonb_build_object(
                        'primaryRateSettings',
                        jsonb_build_object(
                            'rateScript', "StoreBlob"->'rateScript',
                            'rateScripting', COALESCE("StoreBlob"->'rateScripting', 'false'::JSONB),
                            'preferredExchange', "StoreBlob"->'preferredExchange'
                        )
                    )
                    - 'rateScript'
                    - 'rateScripting'
                    - 'preferredExchange'
                WHERE "StoreBlob" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
