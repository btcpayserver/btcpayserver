using Microsoft.EntityFrameworkCore.Migrations;

using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261118085602_AddPlannedPayjoinCleanup")]
    public partial class AddPlannedPayjoinCleanup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "PayjoinOutpoints",
                table: "PlannedTransactions",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayjoinTransactionId",
                table: "PlannedTransactions",
                type: "text",
                nullable: true);
        }
    }
}
