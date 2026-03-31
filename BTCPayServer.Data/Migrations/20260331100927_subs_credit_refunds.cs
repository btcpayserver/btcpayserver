using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260331100927_subs_credit_refunds")]
    /// <inheritdoc />
    public partial class subs_credit_refunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subs_credit_refunds",
                columns: table => new
                {
                    pull_payment_id = table.Column<string>(type: "text", nullable: false),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    deducted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_credit_refunds", x => x.pull_payment_id);
                    table.ForeignKey(
                        name: "FK_subs_credit_refunds_subs_subscribers_subscriber_id",
                        column: x => x.subscriber_id,
                        principalTable: "subs_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subs_credit_refunds_subscriber_id",
                table: "subs_credit_refunds",
                column: "subscriber_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subs_credit_refunds");
        }
    }
}
