using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260203123000_AddWalletRoles")]
    public partial class AddWalletRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "StoreRoles" ("Id", "Role", "Permissions")
                VALUES
                    ('Wallet Manager', 'Wallet Manager', ARRAY['btcpay.store.canmanagewallets']::TEXT[]),
                    ('Multisigner', 'Multisigner', ARRAY['btcpay.store.canmanagewallettransactions']::TEXT[]),
                    ('Multisigner Guest', 'Multisigner Guest', ARRAY['btcpay.store.cansigntransactions']::TEXT[])
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do not delete by name here: these default role names may already belong to user-created roles.
        }
    }
}
