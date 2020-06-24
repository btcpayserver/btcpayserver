using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200624051926_invoicerefund")]
    public partial class invoicerefund : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PullPaymentDataId",
                table: "Invoices",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PullPaymentDataId",
                table: "Invoices",
                column: "PullPaymentDataId");
            if (this.SupportAddForeignKey(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.AddForeignKey(
                    name: "FK_Invoices_PullPayments_PullPaymentDataId",
                    table: "Invoices",
                    column: "PullPaymentDataId",
                    principalTable: "PullPayments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_PullPayments_PullPaymentDataId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PullPaymentDataId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PullPaymentDataId",
                table: "Invoices");
        }
    }
}
