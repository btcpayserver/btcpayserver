using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250407133937_pr_expiry")]
    public partial class pr_expiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Expiry",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                 name: "Amount",
                 table: "PaymentRequests",
                 type: "numeric",
                 nullable: false,
                 defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PaymentRequests",
                type: "text",
                nullable: true);
            migrationBuilder.Sql("""
                ALTER TABLE "PaymentRequests" ADD COLUMN "StatusNew" TEXT;
                UPDATE "PaymentRequests" SET "StatusNew" = CASE
                    WHEN "Status" = 0 THEN 'Pending'
                    WHEN "Status" = 1 THEN 'Completed'
                    WHEN "Status" = 2 THEN 'Expired'
                    WHEN "Status" = 3 THEN 'Processing'
                    ELSE NULL
                END;
                ALTER TABLE "PaymentRequests" DROP COLUMN "Status";
                ALTER TABLE "PaymentRequests" RENAME COLUMN "StatusNew" TO "Status";
                ALTER TABLE "PaymentRequests" ALTER COLUMN "Status" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
