using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200508090807_AddArchivedToPaymentRequests")]
    public partial class AddArchivedToPaymentRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Archived",
                table: "PaymentRequests",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropColumn(ActiveProvider))
            {
                migrationBuilder.DropColumn(
                    name: "Archived",
                    table: "PaymentRequests");
            }
        }
    }
}
