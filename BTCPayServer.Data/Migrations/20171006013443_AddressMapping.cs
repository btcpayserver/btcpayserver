using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20171006013443_AddressMapping")]
    public partial class AddressMapping : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            int? maxLength = this.IsMySql(migrationBuilder.ActiveProvider) ? (int?)255 : null;
            migrationBuilder.CreateTable(
                name: "AddressInvoices",
                columns: table => new
                {
                    Address = table.Column<string>(nullable: false, maxLength: this.IsMySql(migrationBuilder.ActiveProvider) ? (int?)512 : null),
                    InvoiceDataId = table.Column<string>(nullable: true, maxLength: maxLength)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressInvoices", x => x.Address);
                    table.ForeignKey(
                        name: "FK_AddressInvoices_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddressInvoices_InvoiceDataId",
                table: "AddressInvoices",
                column: "InvoiceDataId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddressInvoices");
        }
    }
}
