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
            migrationBuilder.Sql("""
                                 ALTER TABLE "PaymentRequests"
                                     ADD COLUMN "Title" TEXT DEFAULT NULL;
                                 UPDATE "PaymentRequests" SET "Title" = "Blob2" ->> 'title', "Blob2" = "Blob2" - 'title'
                                 WHERE "Blob2" ->> 'title' IS NOT NULL;
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
