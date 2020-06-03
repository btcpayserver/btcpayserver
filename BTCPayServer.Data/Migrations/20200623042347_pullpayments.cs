using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200623042347_pullpayments")]
    public partial class pullpayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    PullPaymentDataId = table.Column<string>(nullable: true),
                    State = table.Column<string>(maxLength: 20, nullable: false),
                    PaymentMethodId = table.Column<string>(maxLength: 20, nullable: false),
                    Destination = table.Column<string>(nullable: true),
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payouts");

            migrationBuilder.DropTable(
                name: "PullPayments");
        }
    }
}
