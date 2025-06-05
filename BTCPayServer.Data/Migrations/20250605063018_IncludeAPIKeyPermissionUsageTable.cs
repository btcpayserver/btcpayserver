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
                    ApiKey = table.Column<string>(type: "text", nullable: true),
                    Permission = table.Column<string>(type: "text", nullable: true),
                    LastUsed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyPermissionUsages", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyPermissionUsages");
        }
    }
}
