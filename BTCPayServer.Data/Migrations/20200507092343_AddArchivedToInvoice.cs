using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200507092343_AddArchivedToInvoice")]
    public class AddArchivedToInvoice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Archived",
                table: "Invoices",
                nullable: false,
                defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "Archived",
                table: "PaymentRequests",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Archived",
                table: "Invoices");
            migrationBuilder.DropColumn(
                name: "Archived",
                table: "PaymentRequests");
        }
    }
}
