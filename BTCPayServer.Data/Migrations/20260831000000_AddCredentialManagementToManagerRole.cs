using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260831000000_AddCredentialManagementToManagerRole")]
    public partial class AddCredentialManagementToManagerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "StoreRoles"
                SET "Permissions" = COALESCE("Permissions", ARRAY[]::TEXT[]) || ARRAY['btcpay.store.canmanagestorecredentials']::TEXT[]
                WHERE "Id" = 'Manager'
                  AND "StoreDataId" IS NULL
                  AND NOT (COALESCE("Permissions", ARRAY[]::TEXT[]) @> ARRAY['btcpay.store.canmanagestorecredentials']::TEXT[]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
