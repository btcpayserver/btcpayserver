using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    CryptoCode = table.Column<string>(type: "TEXT", nullable: false),
                    StoreId = table.Column<string>(type: "TEXT", nullable: true),
                    Expiry = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    OutpointsUsed = table.Column<string>(type: migrationBuilder.IsNpgsql()?"TEXT[]": "TEXT", nullable: true),
                    Blob2 = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingTransactions", x => new { x.CryptoCode, x.TransactionId });
                    table.ForeignKey(
                        name: "FK_PendingTransactions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_StoreId",
                table: "PendingTransactions",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingTransactions");
        }
    }
}
