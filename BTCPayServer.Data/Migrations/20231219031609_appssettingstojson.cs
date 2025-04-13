using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20231219031609_appssettingstojson")]
    public partial class appssettingstojson : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Apps\" ALTER COLUMN \"Settings\" TYPE JSONB USING \"Settings\"::JSONB");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
