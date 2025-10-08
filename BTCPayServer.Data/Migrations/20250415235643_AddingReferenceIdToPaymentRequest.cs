using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250407133937_AddingReferenceIdToPaymentRequest")]
    public partial class AddingReferenceIdToPaymentRequest : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferenceId",
                table: "PaymentRequests",
                type: "TEXT",
                nullable: true);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IX_PaymentRequests_StoreDataId_ReferenceId
                ON "PaymentRequests" ("StoreDataId", "ReferenceId")
                WHERE "ReferenceId" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "PaymentRequests");
        }
    }
}
