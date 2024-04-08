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
    [Migration("20230130062447_jsonb2")]
    public partial class jsonb2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Stores\" ALTER COLUMN \"DerivationStrategies\" TYPE JSONB USING \"DerivationStrategies\"::JSONB");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not supported
        }
    }
}
