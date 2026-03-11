using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Data.Migrations;


[DbContext(typeof(ApplicationDbContext))]
[Migration("20260112034124_disable_orphan_stores")]
public class disable_orphan_stores  : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ApplicationDbContextExtensions.GetUpdateStoreNoActiveUserQuery("SELECT DISTINCT \"Id\" store_id FROM \"Stores\""));
    }
}
