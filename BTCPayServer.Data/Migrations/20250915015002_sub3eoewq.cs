using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class sub3eoewq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "success_redirect_url",
                table: "subscriptions_offerings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscriptions_plan_checkouts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    invoice_metadata = table.Column<string>(type: "jsonb", nullable: true, defaultValueSql: "'{}'::jsonb"),
                    new_subscriber_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_plan_checkouts", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_plan_checkouts_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscriptions_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_checkouts_plan_id",
                table: "subscriptions_plan_checkouts",
                column: "plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions_plan_checkouts");

            migrationBuilder.DropColumn(
                name: "success_redirect_url",
                table: "subscriptions_offerings");
        }
    }
}
