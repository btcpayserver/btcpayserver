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
            var policiesType = migrationBuilder.IsNpgsql() ? "TEXT[]" : "TEXT";
            migrationBuilder.CreateTable(
                name: "StoreRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    StoreDataId = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Policies = table.Column<string>(type: policiesType, nullable: false)
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

            object GetPoliciesData(string[] policies)
            {
                if (migrationBuilder.IsNpgsql())
                    return policies;
                return JsonConvert.SerializeObject(policies);
            }

            migrationBuilder.InsertData(
                "StoreRoles",
                columns: new[] { "Id", "Role", "Policies" },
                columnTypes: new[] { "TEXT", "TEXT", policiesType },
                values: new object[,]
                {
                    {
                        "Owner", "Owner", GetPoliciesData(new[]
                        {
                            "btcpay.store.canmodifystoresettings",
                            "btcpay.store.cantradecustodianaccount",
                            "btcpay.store.canwithdrawfromcustodianaccount",
                            "btcpay.store.candeposittocustodianaccount"
                        })
                    },
                    {
                        "Guest", "Guest", GetPoliciesData(new[]
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
