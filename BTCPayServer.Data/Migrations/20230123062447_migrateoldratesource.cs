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
    [Migration("20230123062447_migrateoldratesource")]
    public partial class migrateoldratesource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql("UPDATE \"Stores\" SET \"StoreBlob\"=jsonb_set(\"StoreBlob\", \'{preferredExchange}\', \'{\"oasis_trade\": \"oasisdev\", \"gdax\":\"coinbasepro\", \"coinaverage\":\"coingecko\"}\'::jsonb->(\"StoreBlob\"->>\'preferredExchange\')) WHERE \"StoreBlob\"->>\'preferredExchange\' = ANY (ARRAY[\'oasis_trade\', \'gdax\', \'coinaverage\']);");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not supported
        }
    }
}
