using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.LNbank.Data.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.Plugins.LNbank");

            migrationBuilder.CreateTable(
                name: "Wallets",
                schema: "BTCPayServer.Plugins.LNbank",
                columns: table => new
                {
                    WalletId = table.Column<string>(nullable: false),
                    UserId = table.Column<string>(nullable: true),
                    Name = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.WalletId);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                schema: "BTCPayServer.Plugins.LNbank",
                columns: table => new
                {
                    TransactionId = table.Column<string>(nullable: false),
                    InvoiceId = table.Column<string>(nullable: true),
                    WalletId = table.Column<string>(nullable: true),
                    Amount = table.Column<long>(nullable: false),
                    AmountSettled = table.Column<long>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    PaymentRequest = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_Transactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "BTCPayServer.Plugins.LNbank",
                        principalTable: "Wallets",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_InvoiceId",
                schema: "BTCPayServer.Plugins.LNbank",
                table: "Transactions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletId",
                schema: "BTCPayServer.Plugins.LNbank",
                table: "Transactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                schema: "BTCPayServer.Plugins.LNbank",
                table: "Wallets",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions",
                schema: "BTCPayServer.Plugins.LNbank");

            migrationBuilder.DropTable(
                name: "Wallets",
                schema: "BTCPayServer.Plugins.LNbank");
        }
    }
}
