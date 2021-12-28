using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.LNbank.Data.Migrations
{
    public partial class AddExplicitStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExplicitStatus",
                schema: "BTCPayServer.Plugins.LNbank",
                table: "Transactions",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExplicitStatus",
                schema: "BTCPayServer.Plugins.LNbank",
                table: "Transactions");
        }
    }
}
