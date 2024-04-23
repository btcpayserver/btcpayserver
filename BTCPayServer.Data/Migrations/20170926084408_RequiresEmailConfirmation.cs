using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20170926084408_RequiresEmailConfirmation")]
    public partial class RequiresEmailConfirmation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                        migrationBuilder.AddColumn<bool>(
                name: "RequiresEmailConfirmation",
                table: "AspNetUsers",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresEmailConfirmation",
                table: "AspNetUsers");
        }
    }
}
