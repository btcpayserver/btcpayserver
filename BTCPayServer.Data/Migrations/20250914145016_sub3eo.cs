using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class sub3eo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "renewable",
                table: "subscriptions_plans");

            migrationBuilder.AddColumn<int>(
                name: "trial_days",
                table: "subscriptions_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trial_days",
                table: "subscriptions_plans");

            migrationBuilder.AddColumn<bool>(
                name: "renewable",
                table: "subscriptions_plans",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
