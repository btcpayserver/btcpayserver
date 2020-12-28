using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20201228223651_RenamingInvoiceSearchDataToFollowConvention")]
    public partial class RenamingInvoiceSearchDataToFollowConvention : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceSearchDatas_Invoices_InvoiceDataId",
                table: "InvoiceSearchDatas");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InvoiceSearchDatas",
                table: "InvoiceSearchDatas");

            migrationBuilder.RenameTable(
                name: "InvoiceSearchDatas",
                newName: "InvoiceSearches");

            migrationBuilder.RenameIndex(
                name: "IX_InvoiceSearchDatas_Value",
                table: "InvoiceSearches",
                newName: "IX_InvoiceSearches_Value");

            migrationBuilder.RenameIndex(
                name: "IX_InvoiceSearchDatas_InvoiceDataId",
                table: "InvoiceSearches",
                newName: "IX_InvoiceSearches_InvoiceDataId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InvoiceSearches",
                table: "InvoiceSearches",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceSearches_Invoices_InvoiceDataId",
                table: "InvoiceSearches",
                column: "InvoiceDataId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceSearches_Invoices_InvoiceDataId",
                table: "InvoiceSearches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InvoiceSearches",
                table: "InvoiceSearches");

            migrationBuilder.RenameTable(
                name: "InvoiceSearches",
                newName: "InvoiceSearchDatas");

            migrationBuilder.RenameIndex(
                name: "IX_InvoiceSearches_Value",
                table: "InvoiceSearchDatas",
                newName: "IX_InvoiceSearchDatas_Value");

            migrationBuilder.RenameIndex(
                name: "IX_InvoiceSearches_InvoiceDataId",
                table: "InvoiceSearchDatas",
                newName: "IX_InvoiceSearchDatas_InvoiceDataId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InvoiceSearchDatas",
                table: "InvoiceSearchDatas",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceSearchDatas_Invoices_InvoiceDataId",
                table: "InvoiceSearchDatas",
                column: "InvoiceDataId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
