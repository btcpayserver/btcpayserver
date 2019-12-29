using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    public partial class ExtendApiKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationIdentifier",
                table: "ApiKeys",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Permissions",
                table: "ApiKeys",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "ApiKeys",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "ApiKeys",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeys_AspNetUsers_UserId",
                table: "ApiKeys",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_AspNetUsers_UserId",
                table: "ApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ApplicationIdentifier",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ApiKeys");
        }
    }
}
