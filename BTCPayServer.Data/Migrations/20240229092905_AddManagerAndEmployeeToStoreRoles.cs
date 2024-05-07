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
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                "StoreRoles",
                columns: new[] { "Id", "Role", "Permissions" },
                columnTypes: new[] { "TEXT", "TEXT", "TEXT[]" },
                values: new object[,]
                {
                    {
                        "Manager", "Manager", new[]
                        {
                            "btcpay.store.canviewstoresettings",
                            "btcpay.store.canmodifyinvoices",
                            "btcpay.store.webhooks.canmodifywebhooks",
                            "btcpay.store.canmodifypaymentrequests",
                            "btcpay.store.canmanagepullpayments",
                            "btcpay.store.canmanagepayouts"
                        }
                    },
                    {
                        "Employee", "Employee", new[]
                        {
                            "btcpay.store.canmodifyinvoices",
                            "btcpay.store.canmodifypaymentrequests",
                            "btcpay.store.cancreatenonapprovedpullpayments",
                            "btcpay.store.canviewpayouts",
                            "btcpay.store.canviewpullpayments"
                        }
                    }
                });
            
            migrationBuilder.UpdateData(
                "StoreRoles",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "TEXT" },
                keyValues: new[] { "Guest" },
                columns: new[] { "Permissions" },
                columnTypes: new[] { "TEXT[]" },
                values: new object[]
                {
                    new[]
                    {
                        "btcpay.store.canmodifyinvoices",
                        "btcpay.store.canviewpaymentrequests",
                        "btcpay.store.canviewpullpayments",
                        "btcpay.store.canviewpayouts"
                    }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData("StoreRoles", "Id", "Manager");
            migrationBuilder.DeleteData("StoreRoles", "Id", "Employee");
            
            migrationBuilder.UpdateData(
                "StoreRoles",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "TEXT" },
                keyValues: new[] { "Guest" },
                columns: new[] { "Permissions" },
                columnTypes: new[] { "TEXT[]" },
                values: new object[]
                {
                    new[]
                    {
                        "btcpay.store.canviewstoresettings",
                        "btcpay.store.canmodifyinvoices",
                        "btcpay.store.canviewcustodianaccounts",
                        "btcpay.store.candeposittocustodianaccount"
                    }
                });
        }
    }
}
