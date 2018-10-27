using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    public partial class CanDeleteStores : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropForeignKey(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropForeignKey(
                name: "FK_AddressInvoices_Invoices_InvoiceDataId",
                table: "AddressInvoices");

                migrationBuilder.DropForeignKey(
                    name: "FK_Apps_Stores_StoreDataId",
                    table: "Apps");

                migrationBuilder.DropForeignKey(
                    name: "FK_Invoices_Stores_StoreDataId",
                    table: "Invoices");

                migrationBuilder.DropForeignKey(
                    name: "FK_Payments_Invoices_InvoiceDataId",
                    table: "Payments");

                migrationBuilder.DropForeignKey(
                    name: "FK_RefundAddresses_Invoices_InvoiceDataId",
                    table: "RefundAddresses");

                migrationBuilder.AddForeignKey(
                    name: "FK_AddressInvoices_Invoices_InvoiceDataId",
                    table: "AddressInvoices",
                    column: "InvoiceDataId",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_ApiKeys_Stores_StoreId",
                    table: "ApiKeys",
                    column: "StoreId",
                    principalTable: "Stores",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_Apps_Stores_StoreDataId",
                    table: "Apps",
                    column: "StoreDataId",
                    principalTable: "Stores",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_Invoices_Stores_StoreDataId",
                    table: "Invoices",
                    column: "StoreDataId",
                    principalTable: "Stores",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_PairedSINData_Stores_StoreDataId",
                    table: "PairedSINData",
                    column: "StoreDataId",
                    principalTable: "Stores",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_Payments_Invoices_InvoiceDataId",
                    table: "Payments",
                    column: "InvoiceDataId",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_PendingInvoices_Invoices_Id",
                    table: "PendingInvoices",
                    column: "Id",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_RefundAddresses_Invoices_InvoiceDataId",
                    table: "RefundAddresses",
                    column: "InvoiceDataId",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AddressInvoices_Invoices_InvoiceDataId",
                table: "AddressInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_Stores_StoreId",
                table: "ApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_Apps_Stores_StoreDataId",
                table: "Apps");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Stores_StoreDataId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PairedSINData_Stores_StoreDataId",
                table: "PairedSINData");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Invoices_InvoiceDataId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PendingInvoices_Invoices_Id",
                table: "PendingInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_RefundAddresses_Invoices_InvoiceDataId",
                table: "RefundAddresses");

            migrationBuilder.AddForeignKey(
                name: "FK_AddressInvoices_Invoices_InvoiceDataId",
                table: "AddressInvoices",
                column: "InvoiceDataId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Apps_Stores_StoreDataId",
                table: "Apps",
                column: "StoreDataId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Stores_StoreDataId",
                table: "Invoices",
                column: "StoreDataId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Invoices_InvoiceDataId",
                table: "Payments",
                column: "InvoiceDataId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefundAddresses_Invoices_InvoiceDataId",
                table: "RefundAddresses",
                column: "InvoiceDataId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
