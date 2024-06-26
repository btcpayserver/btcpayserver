using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20231121031609_removecurrentrefund")]
    public partial class removecurrentrefund : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Refunds_Id_CurrentRefundId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Id_CurrentRefundId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CurrentRefundId",
                table: "Invoices");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
