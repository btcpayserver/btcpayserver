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
            var permissionsType = migrationBuilder.IsNpgsql() ? "TEXT[]" : "TEXT";
            migrationBuilder.CreateTable(
                name: "StoreRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    StoreDataId = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Permissions = table.Column<string>(type: permissionsType, nullable: false)
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

            object GetPermissionsData(string[] permissions)
            {
                if (migrationBuilder.IsNpgsql())
                    return permissions;
                return JsonConvert.SerializeObject(permissions);
            }

            migrationBuilder.InsertData(
                "StoreRoles",
                columns: new[] { "Id", "Role", "Permissions" },
                columnTypes: new[] { "TEXT", "TEXT", permissionsType },
                values: new object[,]
                {
                    {
                        "Owner", "Owner", GetPermissionsData(new[]
                        {
                            "btcpay.store.canmodifystoresettings",
                            "btcpay.store.cantradecustodianaccount",
                            "btcpay.store.canwithdrawfromcustodianaccount",
                            "btcpay.store.candeposittocustodianaccount"
                        })
                    },
                    {
                        "Guest", "Guest", GetPermissionsData(new[]
                        {
                            "btcpay.store.canviewstoresettings",
                            "btcpay.store.canmodifyinvoices",
                            "btcpay.store.canviewcustodianaccounts",
                            "btcpay.store.candeposittocustodianaccount"
                        })
                }
                });

            if (this.SupportAddForeignKey(migrationBuilder.ActiveProvider))
            {
                
                migrationBuilder.AddForeignKey(
                    name: "FK_UserStore_StoreRoles_Role",
                    table: "UserStore",
                    column: "Role",
                    principalTable: "StoreRoles",
                    principalColumn: "Id");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

            if (this.SupportDropForeignKey(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_UserStore_StoreRoles_Role",
                    table: "UserStore");
            }

            migrationBuilder.DropTable(
                name: "StoreRoles");
        }
    }
}
