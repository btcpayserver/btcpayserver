using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240904092905_UpdateStoreOwnerRole")]
    public partial class UpdateStoreOwnerRole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                "StoreRoles",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "TEXT" },
                keyValues: new[] { "Owner" },
                columns: new[] { "Permissions" },
                columnTypes: new[] { "TEXT[]" },
                values: new object[]
                {
                    new[]
                    {
                        "btcpay.store.canmodifystoresettings"
                    }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                "StoreRoles",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "TEXT" },
                keyValues: new[] { "Owner" },
                columns: new[] { "Permissions" },
                columnTypes: new[] { "TEXT[]" },
                values: new object[]
                {
                    new[]
                    {
                        "btcpay.store.canmodifystoresettings",
                        "btcpay.store.cantradecustodianaccount",
                        "btcpay.store.canwithdrawfromcustodianaccount",
                        "btcpay.store.candeposittocustodianaccount"
                    }
                });
        }
    }
}
