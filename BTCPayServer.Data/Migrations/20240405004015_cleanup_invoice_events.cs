using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240405004015_cleanup_invoice_events")]
    public partial class cleanup_invoice_events : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""InvoiceEvents"" DROP CONSTRAINT IF EXISTS ""PK_InvoiceEvents"";
                ALTER TABLE ""InvoiceEvents"" DROP COLUMN IF EXISTS ""UniqueId"";
                CREATE INDEX IF NOT EXISTS ""IX_InvoiceEvents_InvoiceDataId"" ON ""InvoiceEvents""(""InvoiceDataId"");
            ");
            migrationBuilder.Sql(@"VACUUM (FULL, ANALYZE) ""InvoiceEvents"";", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
