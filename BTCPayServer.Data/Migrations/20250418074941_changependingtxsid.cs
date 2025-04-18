using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250418074941_changependingtxsid")]
    public partial class changependingtxsid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PendingTransactions",
                table: "PendingTransactions");

            migrationBuilder.AlterColumn<string>(
                name: "TransactionId",
                table: "PendingTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CryptoCode",
                table: "PendingTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "Id",
                table: "PendingTransactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "PendingTransactions" SET "Id" =
                    lpad(to_hex(trunc(random() * 1e10)::bigint), 8, '0') || '-' ||
                    lpad(to_hex(trunc(random() * 1e10)::bigint), 4, '0') || '-' ||
                    lpad(to_hex(trunc(random() * 1e10)::bigint), 4, '0') || '-' ||
                    lpad(to_hex(trunc(random() * 1e10)::bigint), 4, '0') || '-' ||
                    lpad(to_hex(trunc(random() * 1e10)::bigint), 12, '0');
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PendingTransactions",
                table: "PendingTransactions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_TransactionId",
                table: "PendingTransactions",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
