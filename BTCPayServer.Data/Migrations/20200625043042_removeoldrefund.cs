using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200625043042_removeoldrefund")]
    public partial class removeoldrefund : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefundAddresses");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefundAddresses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Blob = table.Column<byte[]>(type: "BLOB", nullable: true),
                    InvoiceDataId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefundAddresses_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefundAddresses_InvoiceDataId",
                table: "RefundAddresses",
                column: "InvoiceDataId");
        }
    }
}
