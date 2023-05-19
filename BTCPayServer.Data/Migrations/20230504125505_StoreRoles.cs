using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
            migrationBuilder.AddColumn<string>(
                name: "StoreRoleId",
                table: "UserStore",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    StoreDataId = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Policies = table.Column<string>(type: migrationBuilder.IsNpgsql()? "TEXT[]":"TEXT", nullable: false)
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
                name: "IX_UserStore_StoreRoleId",
                table: "UserStore",
                column: "StoreRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRoles_StoreDataId_Role",
                table: "StoreRoles",
                columns: new[] { "StoreDataId", "Role" },
                unique: true);

            if (this.SupportAddForeignKey(migrationBuilder.ActiveProvider))
            {
                
            migrationBuilder.AddForeignKey(
                name: "FK_UserStore_StoreRoles_StoreRoleId",
                table: "UserStore",
                column: "StoreRoleId",
                principalTable: "StoreRoles",
                principalColumn: "Id");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

            if (this.SupportDropForeignKey(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_UserStore_StoreRoles_StoreRoleId",
                    table: "UserStore");
            }

            migrationBuilder.DropTable(
                name: "StoreRoles");

            migrationBuilder.DropIndex(
                name: "IX_UserStore_StoreRoleId",
                table: "UserStore");

            migrationBuilder.DropColumn(
                name: "StoreRoleId",
                table: "UserStore");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "PayoutProcessors");
        }
    }
}
