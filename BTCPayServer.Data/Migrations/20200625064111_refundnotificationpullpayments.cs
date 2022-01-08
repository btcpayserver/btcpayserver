using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200625064111_refundnotificationpullpayments")]
    public partial class refundnotificationpullpayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            int? maxLength = this.IsMySql(migrationBuilder.ActiveProvider) ? (int?)255 : null;

            migrationBuilder.DropTable(
                name: "RefundAddresses");

            migrationBuilder.AddColumn<string>(
                name: "CurrentRefundId",
                table: "Invoices",
                nullable: true,
        maxLength: maxLength);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 36, nullable: false),
                    Created = table.Column<DateTimeOffset>(nullable: false),
                    ApplicationUserId = table.Column<string>(maxLength: 50, nullable: false),
                    NotificationType = table.Column<string>(maxLength: 100, nullable: false),
                    Seen = table.Column<bool>(nullable: false),
                    Blob = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullPayments",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 30, nullable: false),
                    StoreId = table.Column<string>(maxLength: 50, nullable: true),
                    Period = table.Column<long>(nullable: true),
                    StartDate = table.Column<DateTimeOffset>(nullable: false),
                    EndDate = table.Column<DateTimeOffset>(nullable: true),
                    Archived = table.Column<bool>(nullable: false),
                    Blob = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullPayments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 30, nullable: false),
                    Date = table.Column<DateTimeOffset>(nullable: false),
                    PullPaymentDataId = table.Column<string>(maxLength: 30, nullable: true),
                    State = table.Column<string>(maxLength: 20, nullable: false),
                    PaymentMethodId = table.Column<string>(maxLength: 20, nullable: false),
                    Destination = table.Column<string>(maxLength: maxLength, nullable: true),
                    Blob = table.Column<byte[]>(nullable: true),
                    Proof = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payouts_PullPayments_PullPaymentDataId",
                        column: x => x.PullPaymentDataId,
                        principalTable: "PullPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    InvoiceDataId = table.Column<string>(maxLength: maxLength, nullable: false),
                    PullPaymentDataId = table.Column<string>(maxLength: maxLength, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => new { x.InvoiceDataId, x.PullPaymentDataId });
                    table.ForeignKey(
                        name: "FK_Refunds_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Refunds_PullPayments_PullPaymentDataId",
                        column: x => x.PullPaymentDataId,
                        principalTable: "PullPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Id_CurrentRefundId",
                table: "Invoices",
                columns: new[] { "Id", "CurrentRefundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ApplicationUserId",
                table: "Notifications",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_Destination",
                table: "Payouts",
                column: "Destination",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_PullPaymentDataId",
                table: "Payouts",
                column: "PullPaymentDataId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_State",
                table: "Payouts",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_PullPayments_StoreId",
                table: "PullPayments",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PullPaymentDataId",
                table: "Refunds",
                column: "PullPaymentDataId");

            if (this.SupportAddForeignKey(this.ActiveProvider))
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

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Payouts");

            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "PullPayments");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Id_CurrentRefundId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CurrentRefundId",
                table: "Invoices");

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
