using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class subscriptions2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "app_id",
                table: "subscriptions_offerings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_offerings_app_id",
                table: "subscriptions_offerings",
                column: "app_id");

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_offerings_Apps_app_id",
                table: "subscriptions_offerings",
                column: "app_id",
                principalTable: "Apps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_offerings_Apps_app_id",
                table: "subscriptions_offerings");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_offerings_app_id",
                table: "subscriptions_offerings");

            migrationBuilder.DropColumn(
                name: "app_id",
                table: "subscriptions_offerings");
        }
    }
}
