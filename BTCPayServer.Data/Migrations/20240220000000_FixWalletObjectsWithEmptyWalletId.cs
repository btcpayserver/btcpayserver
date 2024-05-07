using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240220000000_FixWalletObjectsWithEmptyWalletId")]
    public partial class FixWalletObjectsWithEmptyWalletId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"WalletObjects\" WHERE \"WalletId\"='';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
