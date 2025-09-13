using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class subscriptions3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_offerings_Stores_store_id",
                table: "subscriptions_offerings");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_offerings_store_id",
                table: "subscriptions_offerings");

            migrationBuilder.DropColumn(
                name: "store_id",
                table: "subscriptions_offerings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "store_id",
                table: "subscriptions_offerings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_offerings_store_id",
                table: "subscriptions_offerings",
                column: "store_id");

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_offerings_Stores_store_id",
                table: "subscriptions_offerings",
                column: "store_id",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
