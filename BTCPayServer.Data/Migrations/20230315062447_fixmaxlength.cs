using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20230315062447_fixmaxlength")]
    public partial class fixmaxlength : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql("ALTER TABLE \"InvoiceSearches\" ALTER COLUMN \"Value\" TYPE TEXT USING \"Value\"::TEXT;");
                migrationBuilder.Sql("ALTER TABLE \"Invoices\"  ALTER COLUMN \"OrderId\" TYPE TEXT USING \"OrderId\"::TEXT;");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not supported
        }
    }
}
