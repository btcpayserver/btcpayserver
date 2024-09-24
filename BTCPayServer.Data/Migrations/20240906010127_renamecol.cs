using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240906010127_renamecol")]
    public partial class renamecol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Destination",
                table: "Payouts",
                newName: "DedupId");

            migrationBuilder.RenameIndex(
                name: "IX_Payouts_Destination_State",
                table: "Payouts",
                newName: "IX_Payouts_DedupId_State");
            migrationBuilder.RenameColumn(
             name: "PaymentMethod",
             table: "PayoutProcessors",
             newName: "PayoutMethodId");

            migrationBuilder.Sql("""
                UPDATE "PayoutProcessors"
                SET
                "PaymentMethodId" = CASE WHEN STRPOS("PaymentMethodId", '_') = 0 THEN "PaymentMethodId" || '-CHAIN'
                                    CASE WHEN STRPOS("PaymentMethodId", '_LightningLike') = 0 THEN "PaymentMethodId" || '-LN'
                                    CASE WHEN STRPOS("PaymentMethodId", '_LNURLPAY') = 0 THEN "PaymentMethodId" || '-LN'
                                    ELSE "PaymentMethodId" END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
