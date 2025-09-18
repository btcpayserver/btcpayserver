using System;
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
            migrationBuilder.DropColumn(
                name: "allow_upgrade",
                table: "subscriptions_plans");

            migrationBuilder.CreateTable(
                name: "subscriptions_portal_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: false),
                    expiration = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_portal_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_portal_sessions_subscriptions_subscribers_sub~",
                        column: x => x.subscriber_id,
                        principalTable: "subscriptions_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_portal_sessions_subscriber_id",
                table: "subscriptions_portal_sessions",
                column: "subscriber_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions_portal_sessions");

            migrationBuilder.AddColumn<bool>(
                name: "allow_upgrade",
                table: "subscriptions_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
