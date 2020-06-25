using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200625060941_refundinvoice3")]
    public partial class refundinvoice3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropForeignKey(migrationBuilder.ActiveProvider))
                migrationBuilder.DropForeignKey(
                    name: "FK_Invoices_PullPayments_PullPaymentDataId",
                    table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PullPaymentDataId",
                table: "Invoices");

            if (this.SupportDropColumn(migrationBuilder.ActiveProvider))
                migrationBuilder.DropColumn(
                name: "PullPaymentDataId",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "CurrentRefundId",
                table: "Invoices",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Id_CurrentRefundId",
                table: "Invoices",
                columns: new[] { "Id", "CurrentRefundId" });

            if (this.SupportAddForeignKey(migrationBuilder.ActiveProvider))
                migrationBuilder.AddForeignKey(
                    name: "FK_Invoices_Refunds_Id_CurrentRefundId",
                    table: "Invoices",
                    columns: new[] { "Id", "CurrentRefundId" },
                    principalTable: "Refunds",
                    principalColumns: new[] { "InvoiceDataId", "PullPaymentDataId" },
                    onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Refunds_Id_CurrentRefundId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Id_CurrentRefundId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CurrentRefundId",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "PullPaymentDataId",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PullPaymentDataId",
                table: "Invoices",
                column: "PullPaymentDataId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_PullPayments_PullPaymentDataId",
                table: "Invoices",
                column: "PullPaymentDataId",
                principalTable: "PullPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
