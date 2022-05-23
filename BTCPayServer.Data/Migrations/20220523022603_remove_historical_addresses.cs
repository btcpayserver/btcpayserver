using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20220523022603_remove_historical_addresses")]
    public partial class remove_historical_addresses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalAddressInvoices");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoricalAddressInvoices",
                columns: table => new
                {
                    InvoiceDataId = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Assigned = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CryptoCode = table.Column<string>(type: "TEXT", nullable: true),
                    UnAssigned = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
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
    }
}
