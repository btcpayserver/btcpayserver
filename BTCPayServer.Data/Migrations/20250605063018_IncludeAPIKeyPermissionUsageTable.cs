using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class IncludeAPIKeyPermissionUsageTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeyPermissionUsages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApiKey = table.Column<string>(type: "text", nullable: false),
                    Permission = table.Column<string>(type: "text", nullable: false),
                    LastUsed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyPermissionUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyPermissionUsages_ApiKey",
                table: "ApiKeyPermissionUsages",
                column: "ApiKey");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyPermissionUsages_ApiKey_Permission",
                table: "ApiKeyPermissionUsages",
                columns: new[] { "ApiKey", "Permission" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeyPermissionUsages_ApiKeys_ApiKey",
                table: "ApiKeyPermissionUsages",
                column: "ApiKey",
                principalTable: "ApiKeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyPermissionUsages");
        }
    }
}
