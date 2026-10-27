using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Data.Migrations;


[DbContext(typeof(ApplicationDbContext))]
[Migration("20260112034124_disable_orphan_stores")]
public class disable_orphan_stores  : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                             WITH
                             stores AS (
                                 SELECT DISTINCT "Id" store_id FROM "Stores"
                             ),
                             active_users AS (
                                 SELECT s."Id",
                                    COUNT(u."Id") FILTER (WHERE u."LockoutEnd" IS NULL OR u."LockoutEnd" <= NOW()) AS active_users
                                 FROM stores st
                                 JOIN "Stores" s ON s."Id" = st.store_id
                                 LEFT JOIN "UserStore" us ON us."StoreDataId" = s."Id"
                                 LEFT JOIN "AspNetUsers" u ON u."Id" = us."ApplicationUserId"
                                 GROUP BY s."Id"
                             ),
                             expected_disabled AS (
                                 SELECT s."Id", COALESCE(s."StoreBlob"->'noActiveUser' = 'true'::JSONB, false) current_value, t.active_users = 0 expected_value FROM active_users t
                                 JOIN "Stores" s ON s."Id" = t."Id"
                             )
                             UPDATE "Stores" s
                             SET "StoreBlob" = jsonb_set("StoreBlob", '{noActiveUser}', to_jsonb(ed.expected_value))
                             FROM expected_disabled ed
                             WHERE s."Id" = ed."Id" AND ed.expected_value <> ed.current_value;
                             """);
    }
}
