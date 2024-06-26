using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NBitcoin;
using Newtonsoft.Json;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20230504125505_StoreRoles")]
    public partial class StoreRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    StoreDataId = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Permissions = table.Column<string>(type: "TEXT[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreRoles_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreRoles_StoreDataId_Role",
                table: "StoreRoles",
                columns: new[] { "StoreDataId", "Role" },
                unique: true);

            migrationBuilder.InsertData(
                "StoreRoles",
                columns: new[] { "Id", "Role", "Permissions" },
                columnTypes: new[] { "TEXT", "TEXT", "TEXT[]" },
                values: new object[,]
                {
                    {
                        "Owner", "Owner", new[]
                        {
                            "btcpay.store.canmodifystoresettings",
                            "btcpay.store.cantradecustodianaccount",
                            "btcpay.store.canwithdrawfromcustodianaccount",
                            "btcpay.store.candeposittocustodianaccount"
                        }
                    },
                    {
                        "Guest", "Guest", new[]
                        {
                            "btcpay.store.canviewstoresettings",
                            "btcpay.store.canmodifyinvoices",
                            "btcpay.store.canviewcustodianaccounts",
                            "btcpay.store.candeposittocustodianaccount"
                        }
                }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_UserStore_StoreRoles_Role",
                table: "UserStore",
                column: "Role",
                principalTable: "StoreRoles",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserStore_StoreRoles_Role",
                table: "UserStore");

            migrationBuilder.DropTable(
                name: "StoreRoles");
        }
    }
}
