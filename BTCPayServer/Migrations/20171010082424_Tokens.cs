using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BTCPayServer.Migrations
{
    public partial class Tokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PairedSINData",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Facade = table.Column<string>(nullable: true),
                    Label = table.Column<string>(nullable: true),
                    Name = table.Column<string>(nullable: true),
                    PairingTime = table.Column<DateTimeOffset>(nullable: false),
                    SIN = table.Column<string>(nullable: true),
                    StoreDataId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairedSINData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PairingCodes",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    DateCreated = table.Column<DateTime>(nullable: false),
                    Expiration = table.Column<DateTimeOffset>(nullable: false),
                    Facade = table.Column<string>(nullable: true),
                    Label = table.Column<string>(nullable: true),
                    Name = table.Column<string>(nullable: true),
                    SIN = table.Column<string>(nullable: true),
                    StoreDataId = table.Column<string>(nullable: true),
                    TokenValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairingCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PairedSINData_SIN",
                table: "PairedSINData",
                column: "SIN");

            migrationBuilder.CreateIndex(
                name: "IX_PairedSINData_StoreDataId",
                table: "PairedSINData",
                column: "StoreDataId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PairedSINData");

            migrationBuilder.DropTable(
                name: "PairingCodes");
        }
    }
}
