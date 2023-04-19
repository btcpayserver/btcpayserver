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
    [Migration("20230125085242_AddForms")]
    public partial class AddForms : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            int? maxlength = migrationBuilder.IsMySql() ? 255 : null;
            migrationBuilder.CreateTable(
                name: "Forms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false, maxLength: maxlength),
                    Name = table.Column<string>(type: "TEXT", nullable: true, maxLength: maxlength),
                    StoreId = table.Column<string>(type: "TEXT", nullable: true, maxLength: maxlength),
                    Config = table.Column<string>(type: migrationBuilder.IsNpgsql() ? "JSONB" : "TEXT", nullable: true),
                    Public = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Forms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Forms_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Forms_StoreId",
                table: "Forms",
                column: "StoreId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Forms");

        }
    }
}
