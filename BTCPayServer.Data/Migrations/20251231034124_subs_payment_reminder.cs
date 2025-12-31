using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251231034124_subs_payment_reminder")]
    public partial class subs_payment_reminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PaymentRequests_Title\";");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminder_date",
                table: "subs_subscribers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                                 UPDATE subs_subscribers
                                 SET reminder_date = COALESCE(period_end, trial_end) - INTERVAL '3 days'
                                 WHERE COALESCE(period_end, trial_end) IS NOT NULL
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reminder_date",
                table: "subs_subscribers");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Title",
                table: "PaymentRequests",
                column: "Title");
        }
    }
}
