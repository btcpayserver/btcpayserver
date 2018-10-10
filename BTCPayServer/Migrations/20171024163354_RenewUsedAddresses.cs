using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BTCPayServer.Migrations
{
    public partial class RenewUsedAddresses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedTime",
                table: "AddressInvoices",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HistoricalAddressInvoices",
                columns: table => new
                {
                    InvoiceDataId = table.Column<string>(nullable: false),
                    Address = table.Column<string>(nullable: false),
                    Assigned = table.Column<DateTimeOffset>(nullable: false),
                    UnAssigned = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalAddressInvoices", x => new { x.InvoiceDataId, x.Address });
                    table.ForeignKey(
                        name: "FK_HistoricalAddressInvoices_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalAddressInvoices");

            migrationBuilder.DropColumn(
                name: "CreatedTime",
                table: "AddressInvoices");
        }
    }
}
