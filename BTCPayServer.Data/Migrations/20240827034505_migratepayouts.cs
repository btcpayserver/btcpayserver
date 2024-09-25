using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240827034505_migratepayouts")]
    [DBScript("002.RefactorPayouts.sql")]
    public partial class migratepayouts : DBScriptsMigration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            base.Up(migrationBuilder);
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
                "PayoutMethodId" = CASE WHEN STRPOS("PayoutMethodId", '_') = 0 THEN "PayoutMethodId" || '-CHAIN'
                                        WHEN STRPOS("PayoutMethodId", '_LightningLike') > 0 THEN split_part("PayoutMethodId", '_LightningLike', 1) || '-LN'
                                        WHEN STRPOS("PayoutMethodId", '_LNURLPAY') > 0 THEN split_part("PayoutMethodId",'_LNURLPAY', 1) || '-LN'
                                        ELSE "PayoutMethodId" END
                """);
        }
    }
}
