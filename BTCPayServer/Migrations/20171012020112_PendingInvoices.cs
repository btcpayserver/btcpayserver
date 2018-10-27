using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BTCPayServer.Migrations
{
    public partial class PendingInvoices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropColumn(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropColumn(
                    name: "Name",
                    table: "PairingCodes");

                migrationBuilder.DropColumn(
                    name: "Name",
                    table: "PairedSINData");
            }
            migrationBuilder.CreateTable(
                name: "PendingInvoices",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingInvoices", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingInvoices");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PairingCodes",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PairedSINData",
                nullable: true);
        }
    }
}
