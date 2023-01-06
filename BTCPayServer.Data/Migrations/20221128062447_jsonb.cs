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
    [Migration("20221128062447_jsonb")]
    public partial class jsonb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql("ALTER TABLE \"Settings\" ALTER COLUMN \"Value\" TYPE JSONB USING \"Value\"::JSONB");
                migrationBuilder.Sql("ALTER TABLE \"Stores\" ALTER COLUMN \"StoreBlob\" TYPE JSONB USING regexp_replace(convert_from(\"StoreBlob\",'UTF8'), '\\\\u0000', '', 'g')::JSONB");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not supported
        }
    }
}
