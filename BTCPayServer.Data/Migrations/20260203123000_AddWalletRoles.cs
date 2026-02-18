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
            migrationBuilder.InsertData(
                "StoreRoles",
                columns: new[] { "Id", "Role", "Permissions" },
                columnTypes: new[] { "TEXT", "TEXT", "TEXT[]" },
                values: new object[,]
                {
                    {
                        "Wallet Manager", "Wallet Manager", new[]
                        {
                            "btcpay.store.canmanagewallets"
                        }
                    },
                    {
                        "Multisigner", "Multisigner", new[]
                        {
                            "btcpay.store.canmodifybitcoinonchain",
                            "btcpay.store.canviewwallet",
                            "btcpay.store.canmanagewallettransactions"
                        }
                    },
                    {
                        "Multisigner Guest", "Multisigner Guest", new[]
                        {
                            "btcpay.store.canmodifybitcoinonchain",
                            "btcpay.store.canviewwallet",
                            "btcpay.store.cansigntransactions"
                        }
                    }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData("StoreRoles", "Id", "Wallet Manager");
            migrationBuilder.DeleteData("StoreRoles", "Id", "Multisigner");
            migrationBuilder.DeleteData("StoreRoles", "Id", "Multisigner Guest");
        }
    }
}
