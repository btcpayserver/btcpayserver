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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
