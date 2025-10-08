using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class subs3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CustomId",
                table: "subscriptions_entitlements",
                newName: "custom_id");

            migrationBuilder.RenameIndex(
                name: "IX_subscriptions_entitlements_offering_id_CustomId",
                table: "subscriptions_entitlements",
                newName: "IX_subscriptions_entitlements_offering_id_custom_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "custom_id",
                table: "subscriptions_entitlements",
                newName: "CustomId");

            migrationBuilder.RenameIndex(
                name: "IX_subscriptions_entitlements_offering_id_custom_id",
                table: "subscriptions_entitlements",
                newName: "IX_subscriptions_entitlements_offering_id_CustomId");
        }
    }
}
