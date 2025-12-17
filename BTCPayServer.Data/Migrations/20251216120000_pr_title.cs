using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251216120000_pr_title")]
    public partial class pr_title : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "PaymentRequests",
                type: "text",
                nullable: true);

            // Create case-insensitive index for Title searches
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PaymentRequests_Title"" ON ""PaymentRequests"" (LOWER(""Title""));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "PaymentRequests");
        }
    }
}
