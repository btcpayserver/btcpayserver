using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20231219031609_translationsmigration")]
    public partial class translationsmigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql("CREATE TABLE translations (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL)");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
