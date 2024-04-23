using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240405052858_cleanup_address_invoices")]
    public partial class cleanup_address_invoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql(@"
DELETE FROM ""AddressInvoices"" WHERE ""Address"" LIKE '%_LightningLike';
ALTER TABLE ""AddressInvoices"" DROP COLUMN IF EXISTS ""CreatedTime"";
VACUUM (FULL, ANALYZE) ""AddressInvoices"";", true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
