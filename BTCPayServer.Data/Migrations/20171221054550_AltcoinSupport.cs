using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20171221054550_AltcoinSupport")]
    public partial class AltcoinSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            int? maxLength = this.IsMySql(migrationBuilder.ActiveProvider) ? (int?)255 : null;
            migrationBuilder.AddColumn<string>(
                name: "CryptoCode",
                table: "HistoricalAddressInvoices",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CryptoCode",
                table: "HistoricalAddressInvoices");
        }
    }
}
