using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    public partial class ClientToApplicationUserLink : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationUserId",
                table: "OpenIddictApplications",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictApplications_ApplicationUserId",
                table: "OpenIddictApplications",
                column: "ApplicationUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OpenIddictApplications_AspNetUsers_ApplicationUserId",
                table: "OpenIddictApplications",
                column: "ApplicationUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpenIddictApplications_AspNetUsers_ApplicationUserId",
                table: "OpenIddictApplications");

            migrationBuilder.DropIndex(
                name: "IX_OpenIddictApplications_ApplicationUserId",
                table: "OpenIddictApplications");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                table: "OpenIddictApplications");
        }
    }
}
