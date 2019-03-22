using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    public partial class IncreaseLengthInAddressColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>("Address", "AddressInvoices", maxLength: 512);
            migrationBuilder.AlterColumn<string>("Address", "HistoricalAddressInvoices", maxLength: 512);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AlterColumn<string>("Address", "AddressInvoices", maxLength: 450);
            migrationBuilder.AlterColumn<string>("Address", "HistoricalAddressInvoices", maxLength: 450);
        }
    }
}
