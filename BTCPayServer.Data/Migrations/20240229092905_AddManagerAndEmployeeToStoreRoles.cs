using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240229092905_AddManagerAndEmployeeToStoreRoles")]
    public partial class AddManagerAndEmployeeToStoreRoles : Migration
    {
        object GetPermissionsData(MigrationBuilder migrationBuilder, string[] permissions)
        {
            return migrationBuilder.IsNpgsql()
                ? permissions
                : JsonConvert.SerializeObject(permissions);
        }
        
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var permissionsType = migrationBuilder.IsNpgsql() ? "TEXT[]" : "TEXT";
            migrationBuilder.InsertData(
                "StoreRoles",
                columns: new[] { "Id", "Role", "Permissions" },
                columnTypes: new[] { "TEXT", "TEXT", permissionsType },
                values: new object[,]
                {
                    {
                        "Manager", "Manager", GetPermissionsData(migrationBuilder, new[]
                        {
                            "btcpay.store.canviewstoresettings",
                            "btcpay.store.canmodifyinvoices",
                            "btcpay.store.webhooks.canmodifywebhooks",
                            "btcpay.store.canmodifypaymentrequests",
                            "btcpay.store.canmanagepullpayments",
                            "btcpay.store.canmanagepayouts"
                        })
                    },
                    {
                        "Employee", "Employee", GetPermissionsData(migrationBuilder, new[]
                        {
                            "btcpay.store.canmodifyinvoices",
                            "btcpay.store.canmodifypaymentrequests",
                            "btcpay.store.cancreatenonapprovedpullpayments",
                            "btcpay.store.canviewpayouts",
                            "btcpay.store.canviewpullpayments"
                        })
                    }
                });
            
            migrationBuilder.UpdateData(
                "StoreRoles",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "TEXT" },
                keyValues: new[] { "Guest" },
                columns: new[] { "Permissions" },
                columnTypes: new[] { permissionsType },
                values: new object[]
                {
                    GetPermissionsData(migrationBuilder, new[]
                    {
                        "btcpay.store.canmodifyinvoices",
                        "btcpay.store.canviewpaymentrequests",
                        "btcpay.store.canviewpullpayments",
                        "btcpay.store.canviewpayouts"
                    })
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData("StoreRoles", "Id", "Manager");
            migrationBuilder.DeleteData("StoreRoles", "Id", "Employee");
            
            var permissionsType = migrationBuilder.IsNpgsql() ? "TEXT[]" : "TEXT";
            migrationBuilder.UpdateData(
                "StoreRoles",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "TEXT" },
                keyValues: new[] { "Guest" },
                columns: new[] { "Permissions" },
                columnTypes: new[] { permissionsType },
                values: new object[]
                {
                    GetPermissionsData(migrationBuilder, new[]
                    {
                        "btcpay.store.canviewstoresettings",
                        "btcpay.store.canmodifyinvoices",
                        "btcpay.store.canviewcustodianaccounts",
                        "btcpay.store.candeposittocustodianaccount"
                    })
                });
        }
    }
}
