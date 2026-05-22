using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260521120000_split_pending_transaction_ids")]
    public partial class split_pending_transaction_ids : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TransactionId",
                table: "PendingTransactions",
                newName: "NoSignatureTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_PendingTransactions_TransactionId",
                table: "PendingTransactions",
                newName: "IX_PendingTransactions_NoSignatureTransactionId");

            migrationBuilder.AddColumn<string>(
                name: "TransactionId",
                table: "PendingTransactions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "PendingTransactions");

            migrationBuilder.RenameIndex(
                name: "IX_PendingTransactions_NoSignatureTransactionId",
                table: "PendingTransactions",
                newName: "IX_PendingTransactions_TransactionId");

            migrationBuilder.RenameColumn(
                name: "NoSignatureTransactionId",
                table: "PendingTransactions",
                newName: "TransactionId");
        }
    }
}
