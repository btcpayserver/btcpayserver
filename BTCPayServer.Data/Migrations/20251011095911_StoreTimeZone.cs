using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreTimeZone",
                table: "Stores",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreTimeZone",
                table: "Stores");
        }
    }
}
